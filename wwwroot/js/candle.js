// Plotly candlestick with optional indicators (SMA20/50, RSI, MACD) — compact vertical layout
window.Candle = (function () {
  function sma(a,w){const o=new Array(a.length).fill(null);let s=0;for(let i=0;i<a.length;i++){s+=a[i];if(i>=w)s-=a[i-w];if(i>=w-1)o[i]=s/w}return o}
  function ema(a,w){const o=new Array(a.length).fill(null),k=2/(w+1);let p=null;for(let i=0;i<a.length;i++){const v=a[i];if(v==null||!isFinite(v)){o[i]=p;continue}p=(p==null)?v:(v-p)*k+p;o[i]=p}return o}
  function rsi(c,period=14){const o=new Array(c.length).fill(null);let g=0,l=0;for(let i=1;i<c.length;i++){const d=c[i]-c[i-1];g+=Math.max(d,0);l+=Math.max(-d,0);if(i>=period){const pd=c[i-period+1]-c[i-period];g-=Math.max(pd,0);l-=Math.max(-pd,0);const rs=l===0?100:(g/period)/(l/period);o[i]=100-100/(1+rs)}}return o}
  function macd(c,f=12,s=26,si=9){const ef=ema(c,f),es=ema(c,s);const m=c.map((_,i)=>ef[i]!=null&&es[i]!=null?ef[i]-es[i]:null);const sg=ema(m.map(v=>v??0),si).map((v,i)=>m[i]==null?null:v);const h=m.map((v,i)=>v==null||sg[i]==null?null:v-sg[i]);return{macdLine:m,signal:sg,hist:h}}

  // tighter domains (less white at bottom)
  function computeDomains(hasRSI, hasMACD) {
    if (!hasRSI && !hasMACD) {
      return { y: [0.12, 1.00] };
    }
    if (hasRSI && !hasMACD) {
      return { y: [0.52, 1.00], y2: [0.12, 0.40] };        // RSI sits low, small bottom gap
    }
    if (!hasRSI && hasMACD) {
      return { y: [0.52, 1.00], y3: [0.06, 0.38] };        // MACD almost on the bottom
    }
    // both RSI + MACD
    return {
      y:  [0.60, 1.00],     // price
      y2: [0.34, 0.50],     // rsi
      y3: [0.06, 0.28]      // macd (very small bottom gap)
    };
  }

  function render(elementId, payload) {
    if (!window.Plotly) { console.error("Plotly not loaded"); return; }

    const x=payload.dates||[], open=payload.open||[], high=payload.high||[], low=payload.low||[], close=payload.close||[];
    const showSMA20=!!payload.showSMA20, showSMA50=!!payload.showSMA50, showRSI=!!payload.showRSI, showMACD=!!payload.showMACD;

    const traces=[];
    traces.push({
      type:"candlestick", x, open, high, low, close,
      increasing:{line:{color:"#0ea5a3",width:1.6},fillcolor:"rgba(14,165,163,.10)"},
      decreasing:{line:{color:"#e45756",width:1.6},fillcolor:"rgba(228,87,86,.10)"},
      whiskerwidth:0.6,
      hovertemplate:"<b>%{x}</b><br>O: %{open:.2f}<br>H: %{high:.2f}<br>L: %{low:.2f}<br>C: %{close:.2f}<extra></extra>",
      name:"Price", xaxis:"x", yaxis:"y"
    });
    if (showSMA20) traces.push({x, y:sma(close,20), type:"scatter", mode:"lines", line:{width:2}, name:"SMA 20", xaxis:"x", yaxis:"y"});
    if (showSMA50) traces.push({x, y:sma(close,50), type:"scatter", mode:"lines", line:{width:2, dash:"dot"}, name:"SMA 50", xaxis:"x", yaxis:"y"});

    const dom=computeDomains(showRSI, showMACD);

    // dynamic figure height: no wasted space
    const baseH = 360;           // price only
    const addRSI = 120;
    const addMACD = 120;
    const height = baseH + (showRSI?addRSI:0) + (showMACD?addMACD:0);

    const layout={
      title:{text:(payload.title||"").toUpperCase(), x:0, xanchor:"left"},
      plot_bgcolor:"white", paper_bgcolor:"white", dragmode:"zoom", showlegend:true,
      legend:{orientation:"h", x:0, y:1.02, xanchor:"left", yanchor:"bottom"},
      margin:{l:56, r:24, t:28, b:20},   // smaller bottom margin
      height,                             // ← key change
      hovermode:"x unified",
      xaxis:{domain:[0,1], anchor:"y", rangeslider:{visible:false}, showgrid:false,
             showticklabels:(showRSI||showMACD)?false:true,
             showspikes:true, spikemode:"across", spikesnap:"cursor", spikecolor:"rgba(0,0,0,.35)", spikethickness:1},
      yaxis:{domain:dom.y, anchor:"x", showgrid:true, gridcolor:"rgba(0,0,0,.06)", zeroline:false}
    };

    if (showRSI) {
      const r=rsi(close,14);
      traces.push({x, y:r, type:"scatter", mode:"lines", line:{width:1.5, color:"#d43f3a"}, name:"RSI(14)", xaxis:"x2", yaxis:"y2"});
      traces.push({x, y:new Array(x.length).fill(70), type:"scatter", mode:"lines", line:{width:1, dash:"dot", color:"#6c757d"}, xaxis:"x2", yaxis:"y2", hoverinfo:"skip", showlegend:false});
      traces.push({x, y:new Array(x.length).fill(30), type:"scatter", mode:"lines", line:{width:1, dash:"dot", color:"#6c757d"}, xaxis:"x2", yaxis:"y2", hoverinfo:"skip", showlegend:false});
      layout.xaxis2={domain:[0,1], anchor:"y2", matches:"x", showgrid:false,
                     showticklabels:showMACD?false:true, tickformat:"%b %Y",
                     showspikes:true, spikemode:"across", spikesnap:"cursor", spikecolor:"rgba(0,0,0,.35)", spikethickness:1};
      layout.yaxis2={domain: dom.y2 ?? dom.y3, anchor:"x2", range:[0,100], showgrid:true, gridcolor:"rgba(0,0,0,.06)", zeroline:false, title:{text:"RSI", standoff:0}};
    }

    if (showMACD) {
      const m=macd(close,12,26,9);
      traces.push({x, y:m.macdLine, type:"scatter", mode:"lines", line:{width:1.5, color:"#b041b0"}, name:"MACD", xaxis:"x3", yaxis:"y3"});
      traces.push({x, y:m.signal,   type:"scatter", mode:"lines", line:{width:1.5, dash:"dot", color:"#6c757d"}, name:"Signal", xaxis:"x3", yaxis:"y3"});
      traces.push({x, y:m.hist,     type:"bar",     marker:{opacity:0.5, color:"#c3c900"}, name:"Hist", xaxis:"x3", yaxis:"y3"});
      layout.xaxis3={domain:[0,1], anchor:"y3", matches:"x", showgrid:false,
                     showticklabels:true, tickformat:"%b %Y",
                     showspikes:true, spikemode:"across", spikesnap:"cursor", spikecolor:"rgba(0,0,0,.35)", spikethickness:1};
      layout.yaxis3={domain: dom.y3 ?? dom.y2, anchor:"x3", showgrid:true, gridcolor:"rgba(0,0,0,.06)", zeroline:true, title:{text:"MACD", standoff:0}};
    }

    const lows=low.filter(v=>v!=null&&isFinite(v)), highs=high.filter(v=>v!=null&&isFinite(v));
    const min=lows.length?Math.min(...lows):(close.length?Math.min(...close):0);
    const max=highs.length?Math.max(...highs):(close.length?Math.max(...close):1);
    const pad=(max-min)*0.06; layout.yaxis.range=[min-pad, max+pad];

    Plotly.react(elementId, traces, layout, {responsive:true, displaylogo:false, modeBarButtonsToRemove:["select2d","lasso2d","autoScale2d"]});
  }

  return { render };
})();
