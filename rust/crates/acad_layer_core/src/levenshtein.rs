/// Compute the Levenshtein edit distance between two strings using character comparisons.
pub fn levenshtein_distance(a: &str, b: &str) -> usize {
    let chars_a: Vec<char> = a.chars().collect();
    let chars_b: Vec<char> = b.chars().collect();

    let m = chars_a.len();
    let n = chars_b.len();

    if m == 0 {
        return n;
    }
    if n == 0 {
        return m;
    }

    let mut d = vec![vec![0usize; n + 1]; m + 1];

    for i in 0..=m {
        d[i][0] = i;
    }
    for j in 0..=n {
        d[0][j] = j;
    }

    for j in 1..=n {
        for i in 1..=m {
            let cost = if chars_a[i - 1] == chars_b[j - 1] {
                0
            } else {
                1
            };
            d[i][j] = (d[i - 1][j] + 1)
                .min(d[i][j - 1] + 1)
                .min(d[i - 1][j - 1] + cost);
        }
    }

    d[m][n]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_levenshtein() {
        assert_eq!(levenshtein_distance("kitten", "sitting"), 3);
        assert_eq!(levenshtein_distance("L-WALL", "L-WALL"), 0);
        assert_eq!(levenshtein_distance("L-WLL", "L-WALL"), 1);
        assert_eq!(levenshtein_distance("", "WALL"), 4);
    }
}
