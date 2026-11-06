use std::cmp::Ordering;

/// Natural, case-insensitive comparison used by Ferret's file lists.
pub fn compare_names(a: &str, b: &str) -> Ordering {
    let (stem_a, extension_a) = split_name(a);
    let (stem_b, extension_b) = split_name(b);

    compare_natural(stem_a, stem_b)
        .then_with(|| compare_natural(extension_a, extension_b))
        .then_with(|| a.to_lowercase().cmp(&b.to_lowercase()))
}

fn split_name(name: &str) -> (&str, &str) {
    if name == ".." {
        return (name, "");
    }
    match name.rfind('.') {
        Some(index) if index > 0 => (&name[..index], &name[index..]),
        _ => (name, ""),
    }
}

#[derive(Debug, PartialEq, Eq)]
enum Token {
    Number(String),
    Text(String),
}

fn tokens(value: &str) -> Vec<Token> {
    let mut result = Vec::new();
    let mut chars = value.chars().peekable();

    while let Some(first) = chars.next() {
        let numeric = first.is_ascii_digit();
        let mut chunk = String::new();
        chunk.push(first);

        while let Some(next) = chars.peek().copied() {
            if next.is_ascii_digit() != numeric {
                break;
            }
            chunk.push(next);
            chars.next();
        }

        result.push(if numeric {
            Token::Number(chunk)
        } else {
            Token::Text(chunk)
        });
    }

    result
}

pub fn compare_natural(a: &str, b: &str) -> Ordering {
    let left = tokens(a);
    let right = tokens(b);
    let shared = left.len().min(right.len());

    for index in 0..shared {
        let ordering = match (&left[index], &right[index]) {
            (Token::Number(one), Token::Number(two)) => compare_numbers(one, two),
            (Token::Text(one), Token::Text(two)) => one.to_lowercase().cmp(&two.to_lowercase()),
            (Token::Number(one), Token::Text(two)) => one.to_lowercase().cmp(&two.to_lowercase()),
            (Token::Text(one), Token::Number(two)) => one.to_lowercase().cmp(&two.to_lowercase()),
        };

        if ordering != Ordering::Equal {
            return ordering;
        }
    }

    left.len().cmp(&right.len())
}

fn compare_numbers(a: &str, b: &str) -> Ordering {
    let trimmed_a = a.trim_start_matches('0');
    let trimmed_b = b.trim_start_matches('0');

    trimmed_a
        .len()
        .cmp(&trimmed_b.len())
        .then_with(|| trimmed_a.cmp(trimmed_b))
        .then_with(|| a.len().cmp(&b.len()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn numbers_sort_naturally() {
        let mut names = vec!["file10.txt", "file2.txt", "file1.txt"];
        names.sort_by(|a, b| compare_names(a, b));
        assert_eq!(names, ["file1.txt", "file2.txt", "file10.txt"]);
    }

    #[test]
    fn case_and_extension_are_handled() {
        assert_eq!(compare_names("README", "readme"), Ordering::Equal);
        assert_eq!(compare_names("a.md", "a.txt"), Ordering::Less);
    }

    #[test]
    fn leading_zeroes_do_not_break_numeric_order() {
        assert_eq!(compare_natural("007", "8"), Ordering::Less);
        assert_eq!(compare_natural("007", "7"), Ordering::Greater);
    }
}
