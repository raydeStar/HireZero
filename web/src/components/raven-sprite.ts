// The raven is a 32×32 sprite drawn as SNES hardware would: one 15-ink palette, a hue-shifted
// outline, light from the top left, and layers that move only in whole sprite pixels.
// Each layer is a grid of palette keys ('.' is transparent) placed at a row offset.

export const ink:Record<string,string>={
  o:'#0b0a12',k:'#17162b',d:'#232341',m:'#34365f',h:'#4d5595',s:'#8088d4', // plumage, dark to sheen
  n:'#2a2b37',N:'#5d6277',f:'#3b3a45',                                      // beak and feet
  e:'#f5f2ea',p:'#f1ead6',q:'#cdbf9a',v:'#5b4cdb',                          // eye glint, letter, violet seal
  w:'#ffffff',a:'#f2b640',z:'rgba(11,10,18,.16)',                             // balloon, alert, ground shadow
};

type Layer={y:number;rows:string[]};
const layer=(y:number,...rows:string[]):Layer=>({y,rows});

export const art={
  shadow:layer(30,'..........zzzzzzzzzz'),
  legs:layer(26,
    '............ofo.ofo',
    '............ofo.ofo',
    '............ofo.ofo',
    '...........offfoffo'),
  // The body keeps a filled neck under the head, so a head that bobs never opens a gap.
  body:layer(13,
    '.......okddddddddddkkkko',
    '.......okkdddddddddkdkdko',
    '........okkdddddddkdkdkdo',
    '........okkkkkkkkdddddko',
    '.......ohhhhmmdkddddddko',
    '......ohshhhhmmdkdddddko',
    '......ohhhmmmmddkddddkko',
    '......odmmmddmmdkdddkko',
    '.....oddmmddmmmdkddkkko',
    '.....odddmmddmmddkkkko',
    '...oodkddmmmddddkkkkko',
    '.oodkkodddddddkkkkkoo',
    'odmdkkookkddkkkkkoo',
    'oddkko...oooookoo',
    'ooooo'),
  head:layer(2,
    '..............o..o',
    '.............oso.oso',
    '............oohmoohmoo',
    '..........oohhmmmmmmddoo',
    '.........ohhmmmddddddddko',
    '........ohmmdddddddddddkko',
    '........ohmddddddddkkkdkko',
    '.......ohmdddddddddoeddkoooo',
    '.......ohddddddddddoodkkNNnnoo',
    '.......omddddddddddddkkknnnnnNo',
    '.......omdddddddddddkkkknnnnnnno',
    '.......okddddddddddkkkkoonnnnoo',
    '.......okkdddddddddkdkdkooooo',
    '........okkdddddddkdkdkdo'),
  lid:layer(9,'...................dd'),
  // A sealed letter held in the beak: work that is approved and ready to go out.
  letter:layer(14,
    '........................ooooooo',
    '........................oqpppqo',
    '........................opqvqpo',
    '........................opppppo',
    '........................ooooooo'),
  balloon:layer(0,
    '........................ooooooo',
    '.......................owwwwwwwo',
    '.......................owwwwwwwo',
    '.......................owwwwwwwo',
    '........................owooooo',
    '........................oo'),
  dot1:layer(2,'.........................o'),
  dot2:layer(2,'...........................o'),
  dot3:layer(2,'.............................o'),
  alert:layer(0,
    '..........................oooo',
    '..........................oaao',
    '..........................oaao',
    '..........................oaao',
    '..........................oooo',
    '..........................oaao',
    '..........................oooo'),
  zSmall:layer(4,'.........................sss','..........................s','.........................sss'),
  zLarge:layer(0,'............................ssss','..............................s','.............................s','............................ssss'),
};
export type LayerName=keyof typeof art;

/** One SVG path per ink, built from horizontal runs so a layer is a handful of elements. */
function paths({y,rows}:Layer){
  const byInk=new Map<string,string>();
  rows.forEach((row,dy)=>{
    for(let x=0;x<row.length;){
      const key=row[x];let end=x+1;while(end<row.length&&row[end]===key)end++;
      if(key!=='.')byInk.set(key,(byInk.get(key)||'')+`M${x} ${y+dy}h${end-x}v1h-${end-x}z`);
      x=end;
    }
  });
  return [...byInk].map(([key,d])=>({key,d}));
}
export const sprite=Object.fromEntries(Object.entries(art).map(([name,value])=>[name,paths(value)])) as Record<LayerName,{key:string;d:string}[]>;
