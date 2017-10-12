const temp = require('./temp1.js');

const url = "http://www.uni-regensburg.de/Fakultaeten/phil_Fak_II/Psychologie/Psy_II/beautycheck/english/durchschnittsgesichter/m(01-32)_gr.jpg";

body =  temp.getCaptionFromUrl(url);
console.log(body);