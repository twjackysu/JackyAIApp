export function playVoiceTube(word: string) {
  const url = `https://tw.voicetube.com/player/${encodeURIComponent(word)}.mp3`;
  new Audio(url).play();
}

export function playGoogleNormal(sentence: string) {
  const url = `/api/Audio/normal?text=${encodeURIComponent(sentence)}`;
  new Audio(url).play();
}

export function playGoogleSlow(sentence: string) {
  const url = `/api/Audio/slow?text=${encodeURIComponent(sentence)}`;
  new Audio(url).play();
}
