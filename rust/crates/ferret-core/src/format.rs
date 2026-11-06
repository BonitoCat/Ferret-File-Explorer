use std::fs;
use std::time::{SystemTime, UNIX_EPOCH};

pub fn format_size(bytes: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    if bytes < 1024 {
        return format!("{bytes} B");
    }

    let mut value = bytes as f64;
    let mut unit = 0usize;
    while value >= 1024.0 && unit + 1 < UNITS.len() {
        value /= 1024.0;
        unit += 1;
    }

    let text = format!("{value:.2}");
    let text = text.trim_end_matches('0').trim_end_matches('.');
    format!("{text} {}", UNITS[unit])
}

/// Human-readable UTC timestamp. Local-time conversion would require a
/// timezone database; properties dialogs label the value as UTC instead.
pub fn format_time(time: SystemTime) -> String {
    let (year, month, day, hour, minute, second) = utc_parts(time);
    format!("{year:04}-{month:02}-{day:02} {hour:02}:{minute:02}:{second:02} UTC")
}

pub fn iso_utc(time: SystemTime) -> String {
    let (year, month, day, hour, minute, second) = utc_parts(time);
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}")
}

fn utc_parts(time: SystemTime) -> (i64, i64, i64, i64, i64, i64) {
    let seconds = time
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs() as i64)
        .unwrap_or(0);
    civil_from_seconds(seconds)
}

fn civil_from_seconds(seconds: i64) -> (i64, i64, i64, i64, i64, i64) {
    let days = seconds.div_euclid(86_400);
    let second_of_day = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let hour = second_of_day / 3_600;
    let minute = (second_of_day % 3_600) / 60;
    let second = second_of_day % 60;
    (year, month, day, hour, minute, second)
}

fn civil_from_days(days: i64) -> (i64, i64, i64) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let day_of_era = z - era * 146_097;
    let year_of_era =
        (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let mut year = year_of_era + era * 400;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_prime = (5 * day_of_year + 2) / 153;
    let day = day_of_year - (153 * month_prime + 2) / 5 + 1;
    let month = month_prime + if month_prime < 10 { 3 } else { -9 };
    if month <= 2 {
        year += 1;
    }
    (year, month, day)
}

pub fn format_permissions(mode: u32) -> String {
    let mut text = String::with_capacity(9);
    for shift in [6, 3, 0] {
        text.push(if mode & (0o4 << shift) != 0 { 'r' } else { '-' });
        text.push(if mode & (0o2 << shift) != 0 { 'w' } else { '-' });
        text.push(if mode & (0o1 << shift) != 0 { 'x' } else { '-' });
    }
    text
}

pub fn user_name(uid: u32) -> String {
    lookup_id("/etc/passwd", uid).unwrap_or_else(|| uid.to_string())
}

pub fn group_name(gid: u32) -> String {
    lookup_id("/etc/group", gid).unwrap_or_else(|| gid.to_string())
}

fn lookup_id(path: &str, id: u32) -> Option<String> {
    let text = fs::read_to_string(path).ok()?;
    for line in text.lines() {
        let mut fields = line.split(':');
        let name = fields.next()?;
        let _password = fields.next();
        let uid = fields.next()?.parse::<u32>().ok()?;
        if uid == id {
            return Some(name.to_string());
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn sizes_are_compact() {
        assert_eq!(format_size(0), "0 B");
        assert_eq!(format_size(1024), "1 KB");
        assert_eq!(format_size(1536), "1.5 KB");
        assert_eq!(format_size(1024 * 1024), "1 MB");
    }

    #[test]
    fn epoch_formats_correctly() {
        assert_eq!(
            format_time(UNIX_EPOCH + Duration::from_secs(0)),
            "1970-01-01 00:00:00 UTC"
        );
        assert_eq!(
            iso_utc(UNIX_EPOCH + Duration::from_secs(1_700_000_000)),
            "2023-11-14T22:13:20"
        );
    }

    #[test]
    fn permission_format_matches_ls() {
        assert_eq!(format_permissions(0o755), "rwxr-xr-x");
        assert_eq!(format_permissions(0o644), "rw-r--r--");
    }
}
