<script lang="ts">
  import '../app.css';
  import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query';
  import { onMount } from 'svelte';
  import { page } from '$app/stores';
  import { applyTheme, loadTheme } from '$lib/theme';
  import { initAgentEventSubscription } from '$lib/events/subscribe';
  import Toast from '@mg-ui-core/components/Toast.svelte';

  let { children } = $props();

  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: 1, staleTime: 2000 },
    },
  });

  const nav = [
    { href: '/', label: 'Dashboard' },
    { href: '/tunnels', label: 'Tunnels' },
    { href: '/history', label: 'History' },
    { href: '/split-tunnel', label: 'Split Tunnel' },
    { href: '/wifi', label: 'Wi-Fi' },
    { href: '/network-lock', label: 'Network Lock' },
    { href: '/diagnostics', label: 'Diagnostics' },
    { href: '/reliability', label: 'Reliability' },
    { href: '/settings', label: 'Settings' },
  ];

  onMount(() => {
    applyTheme(loadTheme());
    let cleanup: (() => void) | undefined;
    void initAgentEventSubscription(queryClient).then((fn) => {
      cleanup = fn;
    });

    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key === 'n') {
        e.preventDefault();
        window.location.href = '/tunnels/new';
      }
      if (e.ctrlKey && e.key === 'k') {
        e.preventDefault();
        window.location.href = '/tunnels';
      }
    };
    window.addEventListener('keydown', onKey);
    return () => {
      cleanup?.();
      window.removeEventListener('keydown', onKey);
    };
  });
</script>

<QueryClientProvider client={queryClient}>
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">MasselGUARD</div>
      <nav>
        {#each nav as item}
          <a href={item.href} class:active={$page.url.pathname === item.href}>{item.label}</a>
        {/each}
      </nav>
    </aside>
    <main class="content">
      {@render children()}
    </main>
    <Toast />
  </div>
</QueryClientProvider>

<style>
  .shell {
    display: grid;
    grid-template-columns: var(--mg-sidebar-w) 1fr;
    min-height: 100vh;
  }

  .sidebar {
    border-right: 1px solid var(--mg-border);
    background: var(--mg-surface);
    padding: 1rem 0.75rem;
  }

  .brand {
    font-weight: 800;
    letter-spacing: 0.02em;
    padding: 0.5rem 0.75rem 1rem;
  }

  nav {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  nav a {
    color: var(--mg-text-muted);
    text-decoration: none;
    padding: 0.55rem 0.75rem;
    border-radius: 8px;
    font-weight: 600;
    font-size: 0.92rem;
  }

  nav a:hover,
  nav a.active {
    color: var(--mg-text);
    background: var(--mg-surface-hover);
  }

  nav a.active {
    color: var(--mg-accent);
  }

  .content {
    padding: 1.25rem 1.5rem 2rem;
    overflow: auto;
  }
</style>
