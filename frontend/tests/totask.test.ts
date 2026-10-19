import { describe, expect, it } from 'vitest';
import { toTaskLines } from '../src/lib/totask';

describe('convert to task', () => {
  it('turns lines of any kind into task lines, keeping indents', () => {
    const r = toTaskLines('Call Sam\n- Book room\n  2. Send agenda\n## Review budget\n> Ask Dana\n\n- [ ] Already a task\n- [x] Done one');
    expect(r.text).toBe('- [ ] Call Sam\n- [ ] Book room\n  - [ ] Send agenda\n- [ ] Review budget\n- [ ] Ask Dana\n\n- [ ] Already a task\n- [x] Done one');
    expect(r.changed).toBe(5);
  });

  it('leaves empty markers alone', () => {
    expect(toTaskLines('- ').changed).toBe(0);
  });
});
