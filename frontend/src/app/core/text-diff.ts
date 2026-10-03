export interface DiffSegment {
  text: string;
  changed: boolean;
}

// Minimal common-prefix/common-suffix character diff - enough to highlight "Smith" vs "Smyth"
// as ['Sm', 'i' vs 'y', 'th'] without pulling in a diff library for one UI affordance. Not a
// general-purpose diff (no mid-string alignment), which is fine for short field values like
// names, addresses and dates.
export function diffChars(a: string, b: string): { left: DiffSegment[]; right: DiffSegment[] } {
  let prefix = 0;
  const maxPrefix = Math.min(a.length, b.length);
  while (prefix < maxPrefix && a[prefix] === b[prefix]) {
    prefix++;
  }

  let suffix = 0;
  const maxSuffix = Math.min(a.length, b.length) - prefix;
  while (suffix < maxSuffix && a[a.length - 1 - suffix] === b[b.length - 1 - suffix]) {
    suffix++;
  }

  const build = (s: string): DiffSegment[] => {
    const segments: DiffSegment[] = [];
    if (prefix > 0) segments.push({ text: s.slice(0, prefix), changed: false });
    const middle = s.slice(prefix, s.length - suffix);
    if (middle.length > 0) segments.push({ text: middle, changed: true });
    if (suffix > 0) segments.push({ text: s.slice(s.length - suffix), changed: false });
    return segments;
  };

  return { left: build(a), right: build(b) };
}
