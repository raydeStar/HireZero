/** The Markdown renderer here has no tables (no GFM plugin), so a table would show as raw pipes. Each data row becomes a
 * list item instead: the first cell in bold, then every other cell with its column's heading. Text outside tables is untouched. */
export function tablesToLists(markdown:string):string{
  const lines=markdown.split('\n');
  const out:string[]=[];
  const cells=(line:string)=>line.trim().replace(/^\|/,'').replace(/\|$/,'').split(/(?<!\\)\|/).map(cell=>cell.trim().replace(/\\\|/g,'|').replace(/\s*<br\s*\/?>\s*/gi,' — '));
  const isRow=(line:string|undefined)=>!!line&&/^\s*\|.*\|\s*$/.test(line);
  const isRule=(line:string|undefined)=>!!line&&/^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/.test(line);
  for(let index=0;index<lines.length;index++){
    if(isRow(lines[index])&&isRule(lines[index+1])){
      const headings=cells(lines[index]);
      index+=2;
      const rows:string[]=[];
      while(index<lines.length&&isRow(lines[index])){
        const row=cells(lines[index]);
        const rest=row.slice(1).map((cell,at)=>cell&&cell!=='—'&&cell!=='-'?`${headings[at+1]?headings[at+1]+': ':''}${cell}`:'').filter(Boolean);
        rows.push(`- **${row[0]||'—'}**${rest.length?' — '+rest.join(' · '):''}`);
        index++;
      }
      index--;
      if(out.length&&out[out.length-1].trim()!=='')out.push('');
      out.push(...rows,'');
      continue;
    }
    // Outside a table, <br> is a line break (the renderer shows raw HTML as text).
    out.push(lines[index].replace(/\s*<br\s*\/?>\s*/gi,'  \n'));
  }
  return out.join('\n');
}
