/** HireZero's mark: an H and a slashed zero, drawn rather than typed so the slash never depends on a font. */
export function BrandMark({className}:{className:string}){
  return <span className={className} title="HireZero" aria-hidden="true">
    <svg viewBox="0 0 24 24" focusable="false"><g fill="none" stroke="currentColor" strokeWidth="2.4">
      <path d="M4.3 6.5v11M9.8 6.5v11M4.3 12h5.5"/><rect x="12.9" y="6.5" width="6.8" height="11" rx="3.4"/><path d="M13.9 16.6l4.8-9.2"/>
    </g></svg>
  </span>;
}
