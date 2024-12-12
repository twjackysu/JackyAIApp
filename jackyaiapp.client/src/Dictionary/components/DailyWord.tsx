import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import { styled } from '@mui/material/styles';
import { useGetWordOfTheDayQuery } from '@/apis/dictionaryApis';
import WordCard from '@/components/WordCard';

const DailyWordContainer = styled(Paper)(({ theme }) => ({
  padding: theme.spacing(2),
  maxWidth: '80%',
  color: theme.palette.text.secondary,
  backgroundColor: theme.palette.background.paper,
  boxShadow: theme.shadows[3],
  borderRadius: theme.shape.borderRadius,
  marginBottom: theme.spacing(2),
}));

function DailyWord() {
  const { data, isFetching } = useGetWordOfTheDayQuery();

  if (isFetching) return null;
  return (
    <DailyWordContainer>
      <Typography variant="h6" gutterBottom>
        每日一字 (Word of the Day)
      </Typography>
      <WordCard word={data?.data} isHideFavoriteButton isOneWordFullWidth />
    </DailyWordContainer>
  );
}

export default DailyWord;
