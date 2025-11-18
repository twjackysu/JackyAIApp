import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import {
  ClozeTest,
  TranslationQualityGradingAssistantResponse,
  TranslationTestResponse,
  TranslationTestUserResponse,
} from '@/apis/dictionaryApis/types';

import { ApiOkResponse } from '../types';

import {
  ConversationStartRequest,
  ConversationStartResponse,
  ConversationResponseRequest,
  ConversationResponseResponse,
  WhisperTranscriptionResponse,
  SentenceTestResponse,
  SentenceTestUserResponse,
  SentenceTestGradingResponse,
} from './types';

// Define a service using a base URL and expected endpoints
export const examApis = createApi({
  reducerPath: 'examApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/exam',
  }),
  endpoints: (builder) => ({
    getClozeTest: builder.query<ApiOkResponse<ClozeTest>, void>({
      query: () => ({
        url: 'cloze',
      }),
    }),
    getTranslationTest: builder.query<ApiOkResponse<TranslationTestResponse>, void>({
      query: () => ({
        url: 'translation',
      }),
    }),
    getTranslationQualityGrading: builder.query<
      ApiOkResponse<TranslationQualityGradingAssistantResponse>,
      TranslationTestUserResponse
    >({
      query: (body) => ({
        url: 'translation/quality_grading',
        method: 'POST',
        body,
      }),
    }),
    startConversationTest: builder.mutation<
      ApiOkResponse<ConversationStartResponse>,
      ConversationStartRequest
    >({
      query: (body) => ({
        url: 'conversation/start',
        method: 'POST',
        body,
      }),
    }),
    respondToConversation: builder.mutation<
      ApiOkResponse<ConversationResponseResponse>,
      ConversationResponseRequest
    >({
      query: (body) => ({
        url: 'conversation/respond',
        method: 'POST',
        body,
      }),
    }),
    transcribeAudio: builder.mutation<ApiOkResponse<WhisperTranscriptionResponse>, FormData>({
      query: (formData) => ({
        url: 'whisper/transcribe',
        method: 'POST',
        body: formData,
      }),
    }),
    getSentenceTest: builder.query<ApiOkResponse<SentenceTestResponse>, void>({
      query: () => ({
        url: 'sentence',
      }),
    }),
    evaluateSentence: builder.mutation<
      ApiOkResponse<SentenceTestGradingResponse>,
      SentenceTestUserResponse
    >({
      query: (body) => ({
        url: 'sentence/evaluate',
        method: 'POST',
        body,
      }),
    }),
  }),
});

export const {
  useGetClozeTestQuery,
  useGetTranslationTestQuery,
  useLazyGetTranslationQualityGradingQuery,
  useStartConversationTestMutation,
  useRespondToConversationMutation,
  useTranscribeAudioMutation,
  useGetSentenceTestQuery,
  useEvaluateSentenceMutation,
} = examApis;
