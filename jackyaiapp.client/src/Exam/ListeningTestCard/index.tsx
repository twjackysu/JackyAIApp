import Box from '@mui/material/Box';

import { useGetListeningTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import ListeningQuizCard from '@/Exam/components/ListeningQuizCard';

/** TOEIC Part 1: look at the picture, pick the description that matches it. */
function ListeningTestCard() {
  const { data, isFetching, refetch, isError, error } = useGetListeningTestQuery();
  const [recordExamResult] = useRecordExamResultMutation();

  const imageUrl = data?.data.imageUrl;

  return (
    <ListeningQuizCard
      question={data?.data}
      isFetching={isFetching}
      isError={isError}
      error={error}
      refetch={refetch}
      legend="聽4句描述，選出最符合圖片的選項:"
      audioUrlForOption={(optionId) =>
        `/api/exam/listening/audio?optionId=${encodeURIComponent(optionId)}`
      }
      onAnswer={(questionId, correct) => {
        recordExamResult({ examType: 'Listening', listeningTestId: questionId, correct })
          .unwrap()
          .catch(() => {
            /* recording is best-effort; ignore failures */
          });
      }}
      renderPrompt={() =>
        imageUrl && (
          <Box
            component="img"
            src={imageUrl}
            alt="Listening question"
            sx={{ width: '100%', borderRadius: 1, mb: 2 }}
          />
        )
      }
    />
  );
}

export default ListeningTestCard;
