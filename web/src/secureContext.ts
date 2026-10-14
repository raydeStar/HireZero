// Over the tailnet HireZero is served as plain HTTP inside Tailscale's encrypted tunnel, which browsers don't treat as a
// secure context, so a few APIs are missing there. Fill in what the app relies on with standard equivalents.
if (typeof crypto !== 'undefined' && typeof crypto.randomUUID !== 'function') {
  Object.defineProperty(crypto, 'randomUUID', { configurable: true, value: () => {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40; bytes[8] = (bytes[8] & 0x3f) | 0x80; // RFC 4122 version 4
    const hex = [...bytes].map(byte => byte.toString(16).padStart(2, '0')).join('');
    return hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-' + hex.slice(12, 16) + '-' + hex.slice(16, 20) + '-' + hex.slice(20);
  } });
}

/** Copy text: the clipboard API where available, otherwise the classic selection copy. */
export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) { await navigator.clipboard.writeText(text); return; }
  const area = document.createElement('textarea'); area.value = text; area.setAttribute('readonly', '');
  area.style.position = 'fixed'; area.style.opacity = '0'; document.body.appendChild(area); area.select();
  const copied = document.execCommand('copy'); area.remove();
  if (!copied) throw new Error('Copy is not available here.');
}
