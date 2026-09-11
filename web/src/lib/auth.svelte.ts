/**
 * State of the "Connect F1 TV" pop-up, shared by everything that can open it
 * (top bar, track map, controls drawer, the tray icon via host events, start-up prompt).
 */
export type ConnectReason = 'first-run' | 'expired' | 'manual';
export type LoginOutcome = 'connected' | 'no-subscription' | 'cancelled';

const PROMPT_KEY = 'pitwall.f1tv.prompt';

class AuthUi {
  open = $state(false);
  reason = $state<ConnectReason>('manual');
  /** Whether the desktop sign-in window is currently open. */
  windowOpen = $state(false);
  lastOutcome = $state<LoginOutcome | null>(null);

  show(reason: ConnectReason = 'manual'): void {
    this.reason = reason;
    this.open = true;
  }

  close(): void {
    this.open = false;
  }

  /** "Don't ask on start-up" — remembered per browser/WebView profile. */
  get promptDisabled(): boolean {
    try {
      return localStorage.getItem(PROMPT_KEY) === 'never';
    } catch {
      return false;
    }
  }

  set promptDisabled(value: boolean) {
    try {
      if (value) localStorage.setItem(PROMPT_KEY, 'never');
      else localStorage.removeItem(PROMPT_KEY);
    } catch {
      /* storage blocked: the prompt simply comes back next time */
    }
  }
}

export const authUi = new AuthUi();
