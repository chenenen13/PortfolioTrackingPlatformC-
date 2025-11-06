// Simple ligne cumulée (vs benchmark) avec Plotly
// payload = { title, s1:{x[],y[],name}, s2:{x[],y[],name}? }
// y = daily returns (ex: 0.004 = 0.4%)
window.renderLineChart = function (elementId, payload) {
  if (!window.Plotly) { console.error("Plotly not loaded"); return; }

  // cumul (1+r1)*(1+r2)*... - 1
  function cumulative(rs) {
    let acc = 1;
    const out = new Array(rs.length);
    for (let i = 0; i < rs.length; i++) { acc *= (1 + (rs[i] ?? 0)); out[i] = acc - 1; }
    return out;
  }

  const traces = [];
  traces.push({
    x: payload.s1.x, y: cumulative(payload.s1.y),
    type: "scatter", mode: "lines", name: payload.s1.name, line: { width: 2 }
  });

  if (payload.s2) {
    traces.push({
      x: payload.s2.x, y: cumulative(payload.s2.y),
      type: "scatter", mode: "lines", name: payload.s2.name, line: { width: 2, dash: "dot" }
    });
  }

  const layout = {
    title: { text: payload.title || "", x: 0, xanchor: "left" },
    plot_bgcolor: "white", paper_bgcolor: "white",
    margin: { l: 56, r: 16, t: 28, b: 40 },
    hovermode: "x unified",
    legend: { orientation: "h", x: 0, y: 1.02, xanchor: "left", yanchor: "bottom" },
    xaxis: { showgrid: false },
    yaxis: { tickformat: ",.0%", showgrid: true, gridcolor: "rgba(0,0,0,.06)", zeroline: false }
  };
  const config = { responsive: true, displaylogo: false, modeBarButtonsToRemove: ["select2d","lasso2d"] };

  Plotly.react(elementId, traces, layout, config);
};
