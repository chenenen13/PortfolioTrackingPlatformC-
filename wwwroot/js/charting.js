window.renderLineChart = (canvasId, labels, series, title) => {
  const ctx = document.getElementById(canvasId);
  if (!ctx) return;

  if (ctx._chart) { ctx._chart.destroy(); }

  ctx._chart = new Chart(ctx, {
    type: 'line',
    data: {
      labels: labels,
      datasets: series.map(s => ({
        label: s.name,
        data: s.values,
        borderWidth: 2,
        fill: false,
        tension: 0.2
      }))
    },
    options: {
      responsive: true,
      plugins: { title: { display: true, text: title } },
      scales: {
        x: { ticks: { autoSkip: true, maxTicksLimit: 12 } },
        y: { beginAtZero: false }
      }
    }
  });
};

window.renderCandleChart = (canvasId, labels, o, h, l, c, title) => {
  const ctx = document.getElementById(canvasId);
  if (!ctx) return;
  if (ctx._chart) { ctx._chart.destroy(); }

  const data = labels.map((d, i) => ({
    x: d,
    o: o[i], h: h[i], l: l[i], c: c[i]
  }));

  ctx._chart = new Chart(ctx, {
    type: 'candlestick',
    data: { datasets: [{ label: title || 'OHLC', data: data, borderWidth: 1 }] },
    options: {
      responsive: true,
      parsing: false,
      plugins: { legend: { display: true } },
      scales: { x: { ticks: { autoSkip: true, maxTicksLimit: 12 } } }
    }
  });
};
