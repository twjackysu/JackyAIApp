import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import {
  ConversationStartRequest,
  ConversationStartResponse,
  ConversationResponseRequest,
  ConversationResponseResponse,
  ConversationSummaryRequest,
  ConversationSummaryResponse,
  WhisperTranscriptionResponse,
  SentenceTestResponse,
  SentenceTestUserResponse,
  SentenceTestGradingResponse,
  RecordExamResultRequest,
  RecordExamResultResponse,
  ListeningTestResponse,
  ListeningBankPageResponse,
  ListeningResponseTestResponse,
  ReadingPassageResponse,
  WritingPromptResponse,
  WritingSubmissionRequest,
  WritingGradingResponse,
  DictationPromptResponse,
  DictationGradeRequest,
  DictationGradeResponse,
} from './types';

import {
  ClozeTest,
  TranslationQualityGradingAssistantResponse,
  TranslationTestResponse,
  TranslationTestUserResponse,
} from '@/apis/dictionaryApis/types';

// Define a service using a base URL and expected endpoints
export const examApis = createApi({
  reducerPath: 'examApis',
  baseQuery: apiBaseQuery('/api/exam'),
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
    summarizeConversation: builder.mutation<
      ApiOkResponse<ConversationSummaryResponse>,
      ConversationSummaryRequest
    >({
      query: (body) => ({
        url: 'conversation/summary',
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
    recordExamResult: builder.mutation<
      ApiOkResponse<RecordExamResultResponse>,
      RecordExamResultRequest
    >({
      query: (body) => ({
        url: 'record-result',
        method: 'POST',
        body,
      }),
    }),
    getListeningTest: builder.query<ApiOkResponse<ListeningTestResponse>, void>({
      query: () => ({
        url: 'listening',
      }),
    }),
    getListeningBank: builder.query<
      ApiOkResponse<ListeningBankPageResponse>,
      { pageNumber: number; pageSize: number }
    >({
      query: ({ pageNumber, pageSize }) => ({
        url: `listening/admin/bank?pageNumber=${pageNumber}&pageSize=${pageSize}`,
      }),
      providesTags: ['ListeningBank'],
    }),
    generateListeningQuestion: builder.mutation<ApiOkResponse<ListeningTestResponse>, void>({
      query: () => ({
        url: 'listening/admin/generate',
        method: 'POST',
      }),
      invalidatesTags: ['ListeningBank'],
    }),
    getListeningResponseTest: builder.query<ApiOkResponse<ListeningResponseTestResponse>, void>({
      query: () => ({
        url: 'listening/response',
      }),
    }),
    getReadingTest: builder.query<ApiOkResponse<ReadingPassageResponse>, void>({
      query: () => ({
        url: 'reading',
      }),
    }),
    getWritingPrompt: builder.query<ApiOkResponse<WritingPromptResponse>, void>({
      query: () => ({
        url: 'writing',
      }),
    }),
    evaluateWriting: builder.mutation<
      ApiOkResponse<WritingGradingResponse>,
      WritingSubmissionRequest
    >({
      query: (body) => ({
        url: 'writing/evaluate',
        method: 'POST',
        body,
      }),
    }),
    getDictationPrompt: builder.query<ApiOkResponse<DictationPromptResponse>, void>({
      query: () => ({
        url: 'dictation',
      }),
    }),
    gradeDictation: builder.mutation<ApiOkResponse<DictationGradeResponse>, DictationGradeRequest>({
      query: (body) => ({
        url: 'dictation/grade',
        method: 'POST',
        body,
      }),
    }),
  }),
  tagTypes: ['ListeningBank'],
});

export const {
  useGetClozeTestQuery,
  useGetTranslationTestQuery,
  useLazyGetTranslationQualityGradingQuery,
  useStartConversationTestMutation,
  useRespondToConversationMutation,
  useSummarizeConversationMutation,
  useTranscribeAudioMutation,
  useGetSentenceTestQuery,
  useEvaluateSentenceMutation,
  useRecordExamResultMutation,
  useGetListeningTestQuery,
  useGetListeningBankQuery,
  useGenerateListeningQuestionMutation,
  useGetListeningResponseTestQuery,
  useGetReadingTestQuery,
  useGetWritingPromptQuery,
  useEvaluateWritingMutation,
  useGetDictationPromptQuery,
  useGradeDictationMutation,
} = examApis;
