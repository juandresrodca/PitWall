/**
 * Bridge to PitWall.exe. Inside the desktop app the page runs in WebView2 and can ask the
 * host to open F1 TV sign-in, swap screens and so on. In a normal browser these are no-ops
 * and the UI hides the controls.
 */
interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
}

declare global {
  interface Window {
    __PITWALL_HOST__?: boolean;
    chrome?: { webview?: WebViewBridge };
  }
}

export type HostCommand =
  | 'connect-f1tv'
  | 'signout-f1tv'
  | 'open-video'
  | 'swap-screens'
  | 'screen-setup'
  | 'fullscreen'
  | 'reload'
  | 'quit';

export const inHost = (): boolean => typeof window !== 'undefined' && !!window.__PITWALL_HOST__ && !!window.chrome?.webview;

export function sendHost(cmd: HostCommand): void {
  window.chrome?.webview?.postMessage({ cmd });
}

/** Opens a link in the user's real browser (desktop) or a new tab (browser). */
export function openExternal(url: string): void {
  if (inHost()) {
    window.chrome!.webview!.postMessage({ cmd: 'open-external', url });
  } else {
    window.open(url, '_blank', 'noopener,noreferrer');
  }
}

export function onHostEvent(listener: (name: string) => void): void {
  window.chrome?.webview?.addEventListener('message', (event) => {
    const name = (event.data as { event?: string })?.event;
    if (name) listener(name);
  });
}
