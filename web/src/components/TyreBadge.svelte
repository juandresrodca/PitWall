<script lang="ts">
  import { compoundLetter, type Compound } from '../lib/model';

  interface Props {
    compound: Compound;
    size?: string;
    used?: boolean;
  }

  let { compound, size = '1.35rem', used = false }: Props = $props();

  const names: Record<Compound, string> = {
    SOFT: 'Soft', MEDIUM: 'Medium', HARD: 'Hard', INTERMEDIATE: 'Intermediate', WET: 'Wet', UNKNOWN: 'Unknown compound',
  };
</script>

<!-- The letter is the identity; the ring colour follows Pirelli's convention. -->
<span class="tyre {compound.toLowerCase()}" class:used style:--size={size} title="{names[compound]}{used ? ', used' : ''}">
  <span aria-hidden="true">{compoundLetter[compound]}</span>
  <span class="sr-only">{names[compound]}</span>
</span>

<style>
  .tyre {
    --c: var(--ink-3);
    display: inline-grid;
    place-items: center;
    width: var(--size);
    height: var(--size);
    border-radius: 50%;
    border: calc(var(--size) * .16) solid var(--c);
    background: #0b0c0f;
    color: var(--ink);
    font-weight: 700;
    font-size: calc(var(--size) * .52);
    line-height: 1;
    flex: none;
  }

  .used { border-style: dashed; }

  .soft { --c: var(--soft); }
  .medium { --c: var(--medium); }
  .hard { --c: var(--hard); }
  .intermediate { --c: var(--inter); }
  .wet { --c: var(--wet); }
</style>
