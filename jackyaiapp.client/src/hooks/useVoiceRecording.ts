import { useEffect, useRef, useState } from 'react';

import { useTranscribeAudioMutation } from '@/apis/examApis';
import { DeliveryFeedback } from '@/apis/examApis/types';

/**
 * A recording stops by itself after this long and is transcribed as it is. A transcription costs one
 * unit of the usage budget whatever its length, while Whisper bills by the minute; an exam answer is
 * a sentence or two. The server refuses a recording over `ConversationPracticeController.MaxAudioBytes`
 * (4 MB), which two minutes at {@link RECORDING_BITS_PER_SECOND} (about 1 MB) stays well under (D-159).
 */
export const MAX_RECORDING_SECONDS = 120;

/** The microphone button's hint before recording, so the two-minute stop is no surprise. */
export const START_RECORDING_LABEL = `點擊開始錄音（最長 ${MAX_RECORDING_SECONDS / 60} 分鐘）`;

/** Said under the input once a recording reached {@link MAX_RECORDING_SECONDS} and stopped by itself. */
export const AUTO_STOPPED_NOTE = `已錄滿 ${MAX_RECORDING_SECONDS / 60} 分鐘，自動停止並轉成文字。`;

/** Said under the input when the recording could not be turned into text. */
export const TRANSCRIPTION_FAILED_NOTE = '語音沒有轉成文字，請再錄一次或直接輸入。';

/** The bitrate asked of the browser's recorder: plenty for speech, and keeps two minutes near 1 MB. */
export const RECORDING_BITS_PER_SECOND = 64_000;

interface UseVoiceRecordingProps {
  onTranscriptionComplete: (text: string, deliveryFeedback?: DeliveryFeedback) => void;
}

/**
 * Microphone capture + Whisper transcription for the Translation and Conversation test cards.
 *
 * The microphone is opened once on mount (so the mic button can be enabled as soon as the
 * device is ready) and is held open between recordings, but it is released on unmount — the
 * returned cleanup stops every track, otherwise the browser keeps showing its "in use"
 * indicator and holds the device for the rest of the session after leaving the exam screen.
 */
export function useVoiceRecording({ onTranscriptionComplete }: UseVoiceRecordingProps) {
  const [isRecording, setIsRecording] = useState(false);
  /** A line for the reader about the last recording (it stopped by itself, or failed); null when there is none. */
  const [recordingNote, setRecordingNote] = useState<string | null>(null);
  const [mediaRecorder, setMediaRecorder] = useState<MediaRecorder | null>(null);
  const [transcribeAudio, { isLoading: isTranscribing }] = useTranscribeAudioMutation();
  const stopTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearStopTimer = () => {
    if (stopTimer.current !== null) {
      clearTimeout(stopTimer.current);
      stopTimer.current = null;
    }
  };

  useEffect(() => {
    // Tracked outside React state so the cleanup below can always reach them, including when
    // the component unmounts while getUserMedia is still pending.
    let cancelled = false;
    let stream: MediaStream | null = null;
    let recorder: MediaRecorder | null = null;

    const releaseMicrophone = () => {
      stream?.getTracks().forEach((track) => track.stop());
      stream = null;
    };

    const setupMediaRecorder = async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        // Unmounted while the permission prompt / device open was still in flight: the stream
        // only exists now, so this is the first (and only) chance to hand the mic back.
        if (cancelled) {
          releaseMicrophone();
          return;
        }

        recorder = new MediaRecorder(stream, { audioBitsPerSecond: RECORDING_BITS_PER_SECOND });

        const chunks: Blob[] = [];

        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) {
            chunks.push(event.data);
          }
        };

        recorder.onstop = async () => {
          if (chunks.length > 0) {
            const audioBlob = new Blob(chunks, { type: 'audio/wav' });
            await handleAudioTranscription(audioBlob);
            chunks.length = 0;
          }
        };

        setMediaRecorder(recorder);
      } catch (error) {
        console.error('Error accessing microphone:', error);
      }
    };

    setupMediaRecorder();

    return () => {
      cancelled = true;
      clearStopTimer();

      if (recorder) {
        // Drop the handlers first: a recording abandoned by navigating away should not fire a
        // transcription request for a screen that no longer exists.
        recorder.ondataavailable = null;
        recorder.onstop = null;
        if (recorder.state !== 'inactive') {
          recorder.stop();
        }
        recorder = null;
      }

      releaseMicrophone();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleAudioTranscription = async (audioBlob: Blob) => {
    try {
      const formData = new FormData();
      formData.append('audioFile', audioBlob, 'recording.wav');

      const response = await transcribeAudio(formData).unwrap();
      if (response.data.text) {
        onTranscriptionComplete(response.data.text, response.data.deliveryFeedback);
      }
    } catch (error) {
      console.error('Error transcribing audio:', error);
      setRecordingNote(TRANSCRIPTION_FAILED_NOTE);
    }
  };

  const toggleRecording = () => {
    if (!mediaRecorder) return;

    if (isRecording) {
      clearStopTimer();
      if (mediaRecorder.state !== 'inactive') {
        mediaRecorder.stop();
      }
      setIsRecording(false);
    } else {
      if (mediaRecorder.state === 'inactive') {
        mediaRecorder.start();
        setIsRecording(true);
        setRecordingNote(null);
        // Stopping fires onstop, which sends what was said so far, as pressing the button would.
        stopTimer.current = setTimeout(() => {
          stopTimer.current = null;
          if (mediaRecorder.state !== 'inactive') {
            mediaRecorder.stop();
          }
          setIsRecording(false);
          setRecordingNote(AUTO_STOPPED_NOTE);
        }, MAX_RECORDING_SECONDS * 1000);
      }
    }
  };

  return {
    isRecording,
    isTranscribing,
    mediaRecorder,
    recordingNote,
    toggleRecording,
  };
}
