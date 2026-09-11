<script lang="ts">
  import { tick } from 'svelte';
  import { feed } from '../lib/feed.svelte';
  import { command } from '../lib/api';
  import { inHost, onHostEvent, sendHost } from '../lib/host';
  import { authUi } from '../lib/auth.svelte';

  const host = inHost();
  const auth = $derived(feed.status.auth);
  const live = $derived(feed.status.source === 'Live');

  type View = 'intro' | 'waiting' | 'verifying' | 'connected' | 'problem' | 'no-subscription' | 'browser';

  const view = $derived.by<View>(() => {
    if (authUi.windowOpen) return 'waiting';
    if (auth.connected || (auth.signedIn && !live)) return 'connected';
    if (auth.signedIn) return 'verifying';
    if (auth.problem) return 'problem';
    if (authUi.lastOutcome === 'no-subscription') return 'no-subscription';
    return host ? 'intro' : 'browser';
  });

  // Host events: the sign-in window can also be opened from the tray icon.
  let hooked = false;
  $effect(() => {
    if (!host || hooked) return;
    hooked = true;
    onHostEvent((name, detail) => {
      if (name === 'login-opened') {
        authUi.windowOpen = true;
        authUi.show(authUi.open ? authUi.reason : 'manual');
      } else if (name === 'login-closed') {
        authUi.windowOpen = false;
        authUi.lastOutcome = detail?.result ?? 'cancelled';
      }
    });
  });

  // Start-up prompt: once per launch, only in the desktop app, only when not signed in.
  let prompted = false;
  $effect(() => {
    if (!host || prompted || !feed.statusSeen) return;
    prompted = true;
    if (auth.signedIn || authUi.promptDisabled) return;
    const reason = auth.problem ? 'expired' : 'first-run';
    setTimeout(() => authUi.show(reason), 1500); // let the dashboard paint first
  });

  // A token that expires mid-weekend brings the pop-up back (once).
  let warnedExpiry = false;
  $effect(() => {
    if (host && !warnedExpiry && auth.problem && auth.subscription && !authUi.open) {
      warnedExpiry = true;
      authUi.show('expired');
    }
  });

  let primary = $state<HTMLButtonElement | null>(null);
  $effect(() => {
    if (authUi.open) tick().then(() => primary?.focus());
  });

  function signIn() {
    authUi.lastOutcome = null;
    sendHost('connect-f1tv');
  }

  async function signOut() {
    if (host) sendHost('signout-f1tv');
    else await command('/api/auth', undefined, 'DELETE');
    authUi.lastOutcome = null;
  }

  // Advanced: paste the login-session cookie value (headless / browser-only setups).
  let showPaste = $state(false);
  let pasted = $state('');
  let pasteError = $state('');
  let saving = $state(false);

  async function savePasted() {
    pasteError = '';
    saving = true;
    try {
      const response = await fetch('/api/auth', {
        method: 'POST',
        headers: { 'X-PitWall': '1', 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: pasted.trim() }),
      });
      if (response.ok) {
        pasted = '';
        showPaste = false;
      } else {
        pasteError = ((await response.json().catch(() => null)) as { error?: string } | null)?.error ?? 'That did not work.';
      }
    } catch {
      pasteError = 'PitWall is not reachable.';
    } finally {
      saving = false;
    }
  }

  const expiresText = $derived.by(() => {
    if (!auth.expires) return '';
    const when = new Date(auth.expires);
    const days = Math.round((when.getTime() - Date.now()) / 86_400_000);
    const date = when.toLocaleString([], { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    return days >= 1 ? `valid until ${date} (${days} day${days === 1 ? '' : 's'})` : `valid until ${date}`;
  });

  const title = $derived(
    view === 'connected' ? 'F1 TV connected'
    : view === 'verifying' ? 'Checking with the timing feed'
    : view === 'waiting' ? 'Finish signing in'
    : view === 'problem' ? (authUi.reason === 'expired' ? 'Your F1 TV sign-in expired' : 'F1 TV needs attention')
    : view === 'no-subscription' ? 'No F1 TV subscription found'
    : 'Connect your F1 TV account',
  );

  function onKey(e: KeyboardEvent) {
    if (authUi.open && e.key === 'Escape') authUi.close();
  }
</script>

<svelte:window onkeydown={onKey} />

{#if authUi.open}
  <div class="scrim" onclick={() => authUi.close()} aria-hidden="true"></div>
  <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="f1tv-title">
    <header>
      <span class="mark" aria-hidden="true"><img src="/favicon.svg" alt="" width="22" height="22" /><i>×</i><b>F1 TV</b></span>
      <button class="x" onclick={() => authUi.close()} aria-label="Close">×</button>
    </header>

    <h2 id="f1tv-title">{title}</h2>

    {#if view === 'intro' || view === 'problem' || view === 'no-subscription'}
      {#if view === 'problem'}
        <p class="alert">{auth.problem}</p>
      {:else if view === 'no-subscription'}
        <p class="alert">You signed in, but Formula 1 didn't issue an F1 TV subscription for that account. Timing still works; car positions and telemetry need F1 TV Access or Pro.</p>
      {:else}
        <p class="lead">Since 2025 Formula 1 streams some live data only to subscribers. Connect once and PitWall unlocks:</p>
      {/if}

      <ul class="unlocks">
        <li><i></i>Live car positions on the track map</li>
        <li><i></i>Speed, gear, throttle and brake per car</li>
        <li><i></i>Championship standings as they would finish</li>
        <li><i></i>Pit-lane and pit-stop times</li>
      </ul>

      <ol class="steps">
        <li><b>1</b><span>A Formula 1 window opens. <strong>Sign in there:</strong> your password goes to formula1.com, never to PitWall.</span></li>
        <li><b>2</b><span>PitWall keeps only the subscription token Formula 1 issues, encrypted for your Windows account.</span></li>
        <li><b>3</b><span>The timing feed reconnects with it. Tokens last about a week; PitWall tells you when to sign in again.</span></li>
      </ol>

      <div class="actions">
        <button class="primary" bind:this={primary} onclick={signIn}>
          {view === 'intro' ? 'Sign in with F1 TV' : 'Sign in again'}
        </button>
        <button class="ghost" onclick={() => authUi.close()}>Not now</button>
      </div>
      {#if authUi.reason === 'first-run'}
        <label class="check">
          <input type="checkbox" checked={authUi.promptDisabled} onchange={(e) => (authUi.promptDisabled = (e.target as HTMLInputElement).checked)} />
          Don't ask when PitWall starts
        </label>
      {/if}
    {:else if view === 'waiting'}
      <div class="waiting">
        <span class="spinner" aria-hidden="true"></span>
        <p>Use the <strong>Formula 1</strong> window to sign in. It closes by itself once you're in.</p>
      </div>
      <div class="actions">
        <button class="primary" bind:this={primary} onclick={() => sendHost('connect-f1tv')}>Show sign-in window</button>
        <button class="ghost" onclick={() => authUi.close()}>Hide this</button>
      </div>
    {:else if view === 'verifying'}
      <div class="waiting">
        <span class="spinner" aria-hidden="true"></span>
        <p>Token saved ({auth.product ?? 'F1 TV'}, {expiresText}). Reconnecting the live timing feed with it…</p>
      </div>
      <div class="actions">
        <button class="primary" bind:this={primary} onclick={() => authUi.close()}>Close</button>
      </div>
    {:else if view === 'connected'}
      <p class="ok"><span class="dot" aria-hidden="true"></span>{auth.product ?? 'F1 TV'} · {expiresText}</p>
      {#if !live}
        <p class="lead">You're replaying a session. The live feed uses your subscription as soon as you switch back to live timing.</p>
      {/if}
      <ul class="unlocks done">
        <li><i></i>Live car positions</li>
        <li><i></i>Car telemetry</li>
        <li><i></i>Championship projection</li>
        <li><i></i>Pit-lane times</li>
      </ul>
      {#if host}
        <p class="lead small">The video screen signs in separately: in the F1 TV window, choose <strong>Sign in</strong> once and Edge remembers it.</p>
      {/if}
      <div class="actions">
        <button class="primary" bind:this={primary} onclick={() => authUi.close()}>Done</button>
        {#if host}<button class="ghost" onclick={() => sendHost('open-video')}>Open F1 TV video</button>{/if}
        <button class="link" onclick={signOut}>Sign out</button>
      </div>
    {:else}
      <p class="lead">You're viewing PitWall in a browser. The sign-in pop-up lives in the desktop app: run <code>PitWall.exe</code> and choose <strong>Connect F1 TV</strong>. The token then works here too.</p>
      <div class="actions">
        <button class="primary" bind:this={primary} onclick={() => (showPaste = !showPaste)}>Paste a token instead</button>
        <button class="ghost" onclick={() => authUi.close()}>Close</button>
      </div>
    {/if}

    {#if view !== 'connected' && view !== 'verifying'}
      {#if host}
        <button class="link advanced" onclick={() => (showPaste = !showPaste)} aria-expanded={showPaste}>
          {showPaste ? 'Hide' : 'Advanced: paste a token'}
        </button>
      {/if}
      {#if showPaste}
        <form class="paste" onsubmit={(e) => { e.preventDefault(); savePasted(); }}>
          <label for="f1tv-token">Value of the <code>login-session</code> cookie from formula1.com, or the bare token</label>
          <textarea id="f1tv-token" bind:value={pasted} rows="3" spellcheck="false" autocomplete="off"></textarea>
          {#if pasteError}<p class="error">{pasteError}</p>{/if}
          <button class="primary" type="submit" disabled={!pasted.trim() || saving}>{saving ? 'Saving…' : 'Save token'}</button>
        </form>
      {/if}
    {/if}
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 60;
    background: rgba(5, 6, 8, .66);
    animation: fade .2s ease;
  }

  .dialog {
    position: fixed;
    left: 50%;
    top: 50%;
    z-index: 61;
    width: min(36rem, 94vw);
    max-height: 92vh;
    overflow-y: auto;
    padding: 1.3rem 1.5rem 1.4rem;
    background: var(--panel);
    border: 1px solid var(--line-2);
    border-top: 3px solid var(--red);
    border-radius: var(--r-panel);
    box-shadow: 0 30px 80px rgba(0, 0, 0, .55);
    transform: translate(-50%, -50%);
    animation: pop .24s var(--ease-out);
  }

  @keyframes fade { from { opacity: 0; } }
  @keyframes pop { from { opacity: 0; transform: translate(-50%, -48%) scale(.97); } }

  header { display: flex; align-items: center; justify-content: space-between; }

  .mark { display: flex; align-items: center; gap: .5rem; }
  .mark img { width: 1.4rem; height: 1.4rem; }
  .mark i { font-style: normal; color: var(--ink-3); }
  .mark b {
    padding: .1rem .45rem;
    background: var(--red);
    color: #fff;
    font-size: .8rem;
    letter-spacing: .06em;
    transform: skewX(var(--skew));
  }

  .x {
    width: 1.8rem;
    height: 1.8rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    background: transparent;
    font-size: 1.1rem;
    line-height: 1;
    transition: transform .16s var(--ease-out), border-color .15s ease;
  }

  h2 { margin: 1rem 0 .5rem; font-size: 1.55rem; line-height: 1.15; }

  .lead { margin: 0 0 .8rem; color: var(--ink-2); font-size: .95rem; }
  .lead.small { font-size: .85rem; }

  .alert {
    margin: 0 0 .9rem;
    padding: .6rem .75rem;
    border-left: 3px solid var(--flag-sc);
    background: color-mix(in oklab, var(--flag-sc) 12%, transparent);
    font-size: .92rem;
  }

  .unlocks {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: .35rem 1rem;
    margin: 0 0 1rem;
    padding: 0;
    list-style: none;
    font-size: .88rem;
  }

  .unlocks li { display: flex; align-items: center; gap: .5rem; }
  .unlocks i { width: .5rem; height: .5rem; background: var(--ink-3); transform: skewX(var(--skew)); flex: none; }
  .unlocks.done i { background: var(--green); }

  .steps {
    margin: 0 0 1.1rem;
    padding: .8rem;
    list-style: none;
    background: var(--panel-2);
    border-radius: var(--r-ctl);
    display: grid;
    gap: .55rem;
    font-size: .86rem;
    color: var(--ink-2);
  }

  .steps li { display: grid; grid-template-columns: 1.5rem 1fr; gap: .5rem; align-items: start; }
  .steps b { font-family: var(--font-num); color: var(--cyan); font-weight: 500; }
  .steps strong { color: var(--ink); }

  .actions { display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; }

  button { font: inherit; }

  .primary, .ghost {
    padding: .55rem 1rem;
    border-radius: var(--r-ctl);
    font-weight: 700;
    font-size: .92rem;
    transition: transform .16s var(--ease-out), filter .15s ease, border-color .15s ease;
  }

  .primary { background: var(--red); border: 1px solid var(--red); color: #fff; }
  .primary:disabled { opacity: .5; cursor: default; }
  .ghost { background: transparent; border: 1px solid var(--line-2); color: var(--ink); }

  .link {
    padding: .3rem 0;
    border: 0;
    background: none;
    color: var(--cyan);
    font-weight: 600;
    font-size: .85rem;
  }

  .actions .link { margin-left: auto; }
  .advanced { display: block; margin-top: 1rem; }

  @media (hover: hover) and (pointer: fine) {
    .primary:hover:not(:disabled) { filter: brightness(1.12); }
    .ghost:hover, .x:hover { border-color: var(--cyan); }
  }

  .check {
    display: flex;
    align-items: center;
    gap: .45rem;
    margin-top: .8rem;
    font-size: .82rem;
    color: var(--ink-3);
  }

  .check input { accent-color: var(--cyan); }

  .waiting {
    display: flex;
    align-items: center;
    gap: .8rem;
    margin: .4rem 0 1.1rem;
    padding: .8rem;
    background: var(--panel-2);
    border-radius: var(--r-ctl);
  }

  .waiting p { margin: 0; font-size: .92rem; color: var(--ink-2); }
  .waiting strong { color: var(--ink); }

  .spinner {
    width: 1.3rem;
    height: 1.3rem;
    border: 2px solid var(--line-2);
    border-top-color: var(--cyan);
    border-radius: 50%;
    animation: spin .8s linear infinite;
    flex: none;
  }

  @keyframes spin { to { transform: rotate(360deg); } }

  .ok {
    display: flex;
    align-items: center;
    gap: .5rem;
    margin: 0 0 .9rem;
    font-weight: 600;
  }

  .dot { width: .6rem; height: .6rem; border-radius: 50%; background: var(--green); }

  .paste { display: grid; gap: .45rem; margin-top: .8rem; }
  .paste label { font-size: .8rem; color: var(--ink-2); }

  textarea {
    width: 100%;
    padding: .5rem;
    background: var(--panel-2);
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    color: var(--ink);
    font-family: var(--font-num);
    font-size: .78rem;
    resize: vertical;
  }

  .paste .primary { justify-self: start; }
  .error { margin: 0; color: var(--red-ink); font-size: .82rem; }

  code { font-family: var(--font-num); font-size: .85em; color: var(--ink); }

  button:active:not(:disabled) { transform: scale(.97); }
</style>
