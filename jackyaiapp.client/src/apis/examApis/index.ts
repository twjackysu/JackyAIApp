import { api, onSuccess } from '../api';
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

export const examApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getClozeTest: builder.query<ApiOkResponse<ClozeTest>, void>({
      query: () => ({
        url: 'exam/cloze',
      }),
    }),
    getTranslationTest: builder.query<ApiOkResponse<TranslationTestResponse>, void>({
      query: () => ({
        url: 'exam/translation',
      }),
    }),
    getTranslationQualityGrading: builder.query<
      ApiOkResponse<TranslationQualityGradingAssistantResponse>,
      TranslationTestUserResponse
    >({
      query: (body) => ({
        url: 'exam/translation/quality_grading',
        method: 'POST',
        body,
      }),
    }),
    startConversationTest: builder.mutation<
      ApiOkResponse<ConversationStartResponse>,
      ConversationStartRequest
    >({
      query: (body) => ({
        url: 'exam/conversation/start',
        method: 'POST',
        body,
      }),
    }),
    respondToConversation: builder.mutation<
      ApiOkResponse<ConversationResponseResponse>,
      ConversationResponseRequest
    >({
      query: (body) => ({
        url: 'exam/conversation/respond',
        method: 'POST',
        body,
      }),
    }),
    summarizeConversation: builder.mutation<
      ApiOkResponse<ConversationSummaryResponse>,
      ConversationSummaryRequest
    >({
      query: (body) => ({
        url: 'exam/conversation/summary',
        method: 'POST',
        body,
      }),
    }),
    transcribeAudio: builder.mutation<ApiOkResponse<WhisperTranscriptionResponse>, FormData>({
      query: (formData) => ({
        url: 'exam/whisper/transcribe',
        method: 'POST',
        body: formData,
      }),
    }),
    getSentenceTest: builder.query<ApiOkResponse<SentenceTestResponse>, void>({
      query: () => ({
        url: 'exam/sentence',
      }),
    }),
    evaluateSentence: builder.mutation<
      ApiOkResponse<SentenceTestGradingResponse>,
      SentenceTestUserResponse
    >({
      query: (body) => ({
        url: 'exam/sentence/evaluate',
        method: 'POST',
        body,
      }),
    }),
    recordExamResult: builder.mutation<
      ApiOkResponse<RecordExamResultResponse>,
      RecordExamResultRequest
    >({
      query: (body) => ({
        url: 'exam/record-result',
        method: 'POST',
        body,
      }),
      // The attempt shows on the activity heatmap, and the server reschedules the tested word.
      invalidatesTags: onSuccess('DueReviews', 'DueCount', 'LearningStats'),
    }),
    getListeningTest: builder.query<ApiOkResponse<ListeningTestResponse>, void>({
      query: () => ({
        url: 'exam/listening',
      }),
    }),
    getListeningBank: builder.query<
      ApiOkResponse<ListeningBankPageResponse>,
      { pageNumber: number; pageSize: number }
    >({
      query: ({ pageNumber, pageSize }) => ({
        url: `exam/listening/admin/bank?pageNumber=${pageNumber}&pageSize=${pageSize}`,
      }),
      providesTags: ['ListeningBank'],
    }),
    generateListeningQuestion: builder.mutation<ApiOkResponse<ListeningTestResponse>, void>({
      query: () => ({
        url: 'exam/listening/admin/generate',
        method: 'POST',
      }),
      invalidatesTags: ['ListeningBank'],
    }),
    getListeningResponseTest: builder.query<ApiOkResponse<ListeningResponseTestResponse>, void>({
      query: () => ({
        url: 'exam/listening/response',
      }),
    }),
    getReadingTest: builder.query<ApiOkResponse<ReadingPassageResponse>, void>({
      query: () => ({
        url: 'exam/reading',
      }),
    }),
    getWritingPrompt: builder.query<ApiOkResponse<WritingPromptResponse>, void>({
      query: () => ({
        url: 'exam/writing',
      }),
    }),
    evaluateWriting: builder.mutation<
      ApiOkResponse<WritingGradingResponse>,
      WritingSubmissionRequest
    >({
      query: (body) => ({
        url: 'exam/writing/evaluate',
        method: 'POST',
        body,
      }),
    }),
    getDictationPrompt: builder.query<ApiOkResponse<DictationPromptResponse>, void>({
      query: () => ({
        url: 'exam/dictation',
      }),
    }),
    gradeDictation: builder.mutation<ApiOkResponse<DictationGradeResponse>, DictationGradeRequest>({
      query: (body) => ({
        url: 'exam/dictation/grade',
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
