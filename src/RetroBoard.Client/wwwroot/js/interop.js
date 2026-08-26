export function copyToClipboard(text) {
    return navigator.clipboard.writeText(text);
}

export function confirmAction(message) {
    return window.confirm(message);
}

// sessionStorage (not localStorage) deliberately: survives a page refresh within the same tab, but
// doesn't linger indefinitely across unrelated future visits once the tab closes.
export function saveSessionItem(key, value) {
    sessionStorage.setItem(key, value);
}

export function loadSessionItem(key) {
    return sessionStorage.getItem(key);
}

// Triggers a browser download of in-memory text content (the §5.7 Markdown export) without a
// server round-trip -- build a Blob, point a throwaway <a download> at it, click it, revoke the URL.
export function downloadFile(filename, content, mimeType) {
    const blob = new Blob([content], { type: mimeType ?? "text/plain" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
}
