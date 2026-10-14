import {ink,sprite,type LayerName} from './raven-sprite';

/** Chip, the employee's raven portrait. A raven is a messenger: its pose says what the employee is doing with your message. */
export type RavenMood='idle'|'listening'|'working'|'letter'|'attention'|'asleep';
const labels:Record<RavenMood,string>={idle:'Ready',listening:'Ready when you are',working:'Working',letter:'Holding work ready to post',attention:'Needs attention',asleep:'Offline'};

function Layer({name,className}:{name:LayerName;className?:string}){
  return <g className={className}>{sprite[name].map(({key,d})=><path key={key} fill={ink[key]} d={d}/>)}</g>;
}

export function Raven({state='idle'}:{state?:RavenMood}){
  const label=labels[state];
  return <span className={'raven '+state} role="img" title={label} aria-label={'Chip: '+label}>
    <svg viewBox="0 0 32 32" shapeRendering="crispEdges" aria-hidden="true" focusable="false">
      <Layer name="shadow"/>
      <g className="raven-bird">
        <Layer name="legs"/>
        <Layer name="body" className="raven-body"/>
        <g className="raven-head">
          <Layer name="head"/>
          <Layer name="lid" className="raven-lid"/>
          {state==='letter'&&<Layer name="letter"/>}
        </g>
      </g>
      {state==='working'&&<g className="raven-balloon"><Layer name="balloon"/><Layer name="dot1" className="raven-dot-1"/><Layer name="dot2" className="raven-dot-2"/><Layer name="dot3" className="raven-dot-3"/></g>}
      {state==='attention'&&<Layer name="alert" className="raven-alert"/>}
      {state==='asleep'&&<g className="raven-sleep"><Layer name="zSmall" className="raven-z-1"/><Layer name="zLarge" className="raven-z-2"/></g>}
    </svg>
  </span>;
}
