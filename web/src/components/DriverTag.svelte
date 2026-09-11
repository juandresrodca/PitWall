<script lang="ts">
  import type { Driver } from '../lib/model';

  interface Props {
    driver: Driver;
    compact?: boolean;
  }

  let { driver, compact = false }: Props = $props();
</script>

<!-- Broadcast-style tag: team colour on a slanted edge, TLA in white. -->
<span class="tag" class:compact style:--team={driver.color} title="{driver.name} · {driver.team}">
  <span class="bar" aria-hidden="true"></span>
  <span class="tla">{driver.tla}</span>
  {#if !compact}<span class="no num">{driver.num}</span>{/if}
</span>

<style>
  .tag {
    display: inline-flex;
    align-items: center;
    gap: .45rem;
    min-width: 0;
  }

  .bar {
    width: .32rem;
    height: 1.2rem;
    background: var(--team);
    transform: skewX(var(--skew));
    flex: none;
  }

  .tla {
    font-weight: 700;
    font-size: 1.02rem;
    letter-spacing: .04em;
  }

  .no {
    font-size: .72rem;
    color: var(--ink-3);
  }

  .compact .bar { height: 1rem; }
  .compact .tla { font-size: .9rem; }
</style>
