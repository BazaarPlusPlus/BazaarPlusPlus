//! JSON written exactly as Python's `json.dumps(sort_keys=True, ensure_ascii=False,
//! separators=(",", ":"))` writes it, so published digests do not depend on the producer.

use anyhow::Result;
use serde::de::{Deserializer, MapAccess, Visitor};
use serde_json::Value;
use std::fmt::Write as _;

/// A number with Python's int and float distinction.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Num {
    I(i64),
    F(f64),
}

impl Num {
    pub fn from_value(v: &Value) -> Option<Self> {
        let n = v.as_number()?;
        n.as_i64().map(Num::I).or_else(|| n.as_f64().map(Num::F))
    }
    pub fn as_f64(self) -> f64 {
        match self {
            Num::I(i) => i as f64,
            Num::F(f) => f,
        }
    }
    pub fn times(self, other: Self) -> Self {
        match (self, other) {
            (Num::I(a), Num::I(b)) => a.checked_mul(b).map_or(Num::F(a as f64 * b as f64), Num::I),
            _ => Num::F(self.as_f64() * other.as_f64()),
        }
    }
    pub fn is_integral(self) -> bool {
        match self {
            Num::I(_) => true,
            Num::F(f) => f.is_finite() && f == f.trunc(),
        }
    }
    /// Python's `number = int(number)` when `number == int(number)`.
    pub fn normalized(self) -> Self {
        match self {
            Num::F(f) if self.is_integral() && f.abs() < 9.2e18 => Num::I(f as i64),
            other => other,
        }
    }
    pub fn to_value(self) -> Value {
        match self {
            Num::I(i) => Value::from(i),
            Num::F(f) => Value::from(f),
        }
    }
}

/// Lowercase hex of a digest.
pub fn hex(bytes: &[u8]) -> String {
    bytes
        .iter()
        .fold(String::with_capacity(bytes.len() * 2), |mut out, b| {
            let _ = write!(out, "{b:02x}");
            out
        })
}

/// Python's `repr(float)`.
pub fn float_repr(f: f64) -> String {
    if f == 0.0 {
        return if f.is_sign_negative() {
            "-0.0".into()
        } else {
            "0.0".into()
        };
    }
    // The shortest round-trip digits; when two candidates of that length are equally near,
    // Python takes the even one, which fixed-precision formatting gives.
    let shortest = format!("{f:e}");
    let digits = shortest
        .split_once('e')
        .map_or(0, |(m, _)| m.chars().filter(char::is_ascii_digit).count());
    let even = format!("{f:.*e}", digits.saturating_sub(1));
    let sci = if even.parse::<f64>() == Ok(f) {
        even
    } else {
        shortest
    };
    let (mantissa, exp) = sci.split_once('e').expect("LowerExp has an exponent");
    let exp: i32 = exp.parse().expect("LowerExp exponent is an integer");
    let negative = mantissa.starts_with('-');
    let digits: String = mantissa.chars().filter(char::is_ascii_digit).collect();
    let mut out = String::new();
    if negative {
        out.push('-');
    }
    if (-4..16).contains(&exp) {
        if exp < 0 {
            out.push_str("0.");
            out.push_str(&"0".repeat((-exp - 1) as usize));
            out.push_str(&digits);
        } else {
            let point = exp as usize + 1;
            if digits.len() <= point {
                out.push_str(&digits);
                out.push_str(&"0".repeat(point - digits.len()));
                out.push_str(".0");
            } else {
                out.push_str(&digits[..point]);
                out.push('.');
                out.push_str(&digits[point..]);
            }
        }
    } else {
        out.push_str(&digits[..1]);
        if digits.len() > 1 {
            out.push('.');
            out.push_str(&digits[1..]);
        }
        let _ = write!(out, "e{}{:02}", if exp < 0 { '-' } else { '+' }, exp.abs());
    }
    out
}

fn string(out: &mut String, s: &str, ascii: bool) {
    out.push('"');
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            '\u{8}' => out.push_str("\\b"),
            '\u{c}' => out.push_str("\\f"),
            c if (c as u32) < 0x20 || (ascii && (c as u32) > 0x7e) => {
                let mut units = [0u16; 2];
                for unit in c.encode_utf16(&mut units) {
                    let _ = write!(out, "\\u{:04x}", unit);
                }
            }
            c => out.push(c),
        }
    }
    out.push('"');
}

/// One JSON string; `ascii` escapes everything past ASCII like `ensure_ascii=True`.
pub fn json_string(s: &str, ascii: bool) -> String {
    let mut out = String::new();
    string(&mut out, s, ascii);
    out
}

fn write(out: &mut String, v: &Value) {
    match v {
        Value::Null => out.push_str("null"),
        Value::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
        Value::Number(n) => {
            if let Some(i) = n.as_i64() {
                let _ = write!(out, "{i}");
            } else if let Some(u) = n.as_u64() {
                let _ = write!(out, "{u}");
            } else {
                out.push_str(&float_repr(n.as_f64().unwrap_or(f64::NAN)));
            }
        }
        Value::String(s) => string(out, s, false),
        Value::Array(a) => {
            out.push('[');
            for (i, item) in a.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                write(out, item);
            }
            out.push(']');
        }
        Value::Object(o) => {
            let mut keys: Vec<&String> = o.keys().collect();
            keys.sort();
            out.push('{');
            for (i, k) in keys.into_iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                string(out, k, false);
                out.push(':');
                write(out, &o[k]);
            }
            out.push('}');
        }
    }
}

/// Sorted keys, no spaces, non-ASCII kept.
pub fn canonical(v: &Value) -> String {
    let mut out = String::new();
    write(&mut out, v);
    out
}

/// A JSON object's members in source order.
pub fn ordered_object(text: &str) -> Result<Vec<(String, Value)>> {
    struct Ordered;
    impl<'de> Visitor<'de> for Ordered {
        type Value = Vec<(String, Value)>;
        fn expecting(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
            f.write_str("a JSON object")
        }
        fn visit_map<A: MapAccess<'de>>(self, mut map: A) -> Result<Self::Value, A::Error> {
            let mut out = Vec::new();
            while let Some((k, v)) = map.next_entry::<String, Value>()? {
                out.push((k, v));
            }
            Ok(out)
        }
    }
    let mut de = serde_json::Deserializer::from_str(text);
    Ok((&mut de).deserialize_map(Ordered)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn floats_read_like_python_repr() {
        for (f, s) in [
            (0.5, "0.5"),
            (100.0, "100.0"),
            (1e-5, "1e-05"),
            (0.0001, "0.0001"),
            (1e16, "1e+16"),
            (1.5e16, "1.5e+16"),
            (123_456_789_012_345.6, "123456789012345.6"),
            (f64::from(0.1_f32), "0.10000000149011612"),
            (-2.25, "-2.25"),
            (f64::from(0.854_835_5_f32), "0.8548355102539062"),
            (f64::from(2.611_f32), "2.6110000610351562"),
        ] {
            assert_eq!(float_repr(f), s);
        }
    }

    #[test]
    fn strings_escape_like_python_without_ascii_escaping() {
        assert_eq!(
            canonical(&serde_json::json!({"b": "é\n\u{1}\"", "a": 1})),
            "{\"a\":1,\"b\":\"é\\n\\u0001\\\"\"}"
        );
    }
}
