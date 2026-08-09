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

// Browser-native TTS (Web Speech API) — synthesized on-device, no backend call and no
// OpenAI cost, unlike playGoogleNormal/playGoogleSlow. Voice quality depends on the
// device/browser's installed voices rather than being consistent like OpenAI TTS.
export function playBrowserTts(text: string, lang = 'en-US') {
  if (!('speechSynthesis' in window)) return;
  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = lang;
  window.speechSynthesis.speak(utterance);
}
