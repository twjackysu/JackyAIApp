export function playVoiceTube(word: string) {
  const url = `https://tw.voicetube.com/player/${encodeURIComponent(word)}.mp3`;
  new Audio(url).play();
}

/**
 * The site's own voice (`/api/audio`, Azure Speech or OpenAI), falling back to the browser's when that fails: the
 * server refused it (the per-address rate limit, the usage budget), the speech service is out of its monthly
 * allowance, or the page is offline (D-173). A press that heard nothing is worse than a plainer voice.
 */
function playSiteVoice(path: 'normal' | 'slow', sentence: string, browserRate: number) {
  const audio = new Audio(`/api/Audio/${path}?text=${encodeURIComponent(sentence)}`);
  let fellBack = false;
  const fallBack = () => {
    if (fellBack) return;
    fellBack = true;
    playBrowserTts(sentence, 'en-US', browserRate);
  };
  // A source that could not be fetched or decoded fires `error` and also rejects `play()`.
  audio.onerror = fallBack;
  audio.play().catch((error: unknown) => {
    const name = error instanceof DOMException || error instanceof Error ? error.name : '';
    // Paused for the next clip: the reader's own doing. Held back until a press: so would the browser's voice be.
    if (name === 'AbortError' || name === 'NotAllowedError') return;
    fallBack();
  });
}

export function playGoogleNormal(sentence: string) {
  playSiteVoice('normal', sentence, 1);
}

export function playGoogleSlow(sentence: string) {
  playSiteVoice('slow', sentence, 0.6);
}

// Browser-native TTS (Web Speech API) — synthesized on-device, no backend call and no
// OpenAI cost, unlike playGoogleNormal/playGoogleSlow. Voice quality depends on the
// device/browser's installed voices rather than being consistent like OpenAI TTS.
export function playBrowserTts(text: string, lang = 'en-US', rate = 1) {
  if (!('speechSynthesis' in window)) return;
  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = lang;
  utterance.rate = rate;
  window.speechSynthesis.speak(utterance);
}
