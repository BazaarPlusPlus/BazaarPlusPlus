import { apiError } from '../errors';

export function oneQueryValue(
  params: URLSearchParams,
  name: string,
  required: boolean
): string | null {
  const values = params.getAll(name);
  if (values.length > 1 || (required && values.length === 0)) {
    throw apiError('invalid_query', `${name} must appear exactly once`, {
      details: {
        field: name
      }
    });
  }
  return values[0] ?? null;
}

const IPV6_GROUP = /^[0-9a-f]{1,4}$/;

function ipv6Groups(text: string): string[] {
  return text === '' ? [] : text.split(':');
}

// An IPv6 client usually owns at least a /64 and can rotate addresses inside it,
// so rate limits count the /64 prefix. Anything unparseable keys on itself.
function ipv6Prefix64(address: string): string | null {
  const halves = address.toLowerCase().split('::');
  if (halves.length > 2) return null;
  const head = ipv6Groups(halves[0]);
  const tail = halves.length === 2 ? ipv6Groups(halves[1]) : [];
  const last = tail.at(-1) ?? head.at(-1) ?? '';
  // A dotted IPv4 tail fills the last two groups, never the /64 prefix.
  const width = head.length + tail.length + (last.includes('.') ? 1 : 0);
  const missing = 8 - width;
  if (halves.length === 1 ? missing !== 0 : missing < 1) return null;
  const full = [
    ...head,
    ...Array<string>(Math.max(missing, 0)).fill('0'),
    ...tail
  ];
  const prefix = full.slice(0, 4);
  if (!prefix.every((group) => IPV6_GROUP.test(group))) return null;
  return `${prefix.map((group) => Number.parseInt(group, 16).toString(16)).join(':')}::/64`;
}

// The per-client key every rate-limit binding counts on.
export function clientRateLimitKey(request: Request): string {
  const address = request.headers.get('CF-Connecting-IP');
  if (address === null || address === '') return 'unknown';
  if (!address.includes(':')) return address;
  return ipv6Prefix64(address) ?? address;
}
