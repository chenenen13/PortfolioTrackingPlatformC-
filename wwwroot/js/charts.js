// Safe Chart.js helper for Blazor Server (no throws when canvas/circuit disappears)
window.blazorCharts = (function () {
  const registry = {};

  function has(id) {
    return !!document.getElementById(id);
  }

  function safeDestroy(id) {
    const ch = registry[id];
    if (!ch) return;
    try {
      const cnv = ch?.ctx?.canvas;
      if (cnv && document.contains(cnv)) ch.destroy();
    } catch { /* ignore */ }
    delete registry[id];
  }

  function toDataset(ds, i) {
    const COLORS = ['#007bff','#ff6b6b','#fca311','#2ec4b6','#9b5de5','#4cc9f0','#ef476f'];
    const c = COLORS[i % COLORS.length];
    return {
      label: ds.label,
      data: ds.data,
      borderColor: c,
      backgroundColor: c + '33',
      pointRadius: 2,
      borderWidth: 2,
      fill: false,
      tension: 0.15
    };
  }

  function renderLineChart(id, labels, datasets, title) {
    const el = document.getElementById(id);
    if (!el) return;

    safeDestroy(id);

    // wait a frame so canvas has dimensions
    requestAnimationFrame(() => {
      const ctx = el.getContext && el.getContext('2d');
      if (!ctx) return;

      registry[id] = new Chart(ctx, {
        type: 'line',
        data: { labels, datasets: datasets.map(toDataset) },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'index', intersect: false },
          plugins: {
            title: { display: !!title, text: title },
            legend: { position: 'top' },
            tooltip: { enabled: true }
          },
          scales: {
            x: { title: { display: true, text: 'Date' } },
            y: { title: { display: true, text: 'Value (normalized)' } }
          }
        }
      });
    });
  }

  return { has, renderLineChart };
})();
