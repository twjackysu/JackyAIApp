import { useGetWordOfTheDayQuery } from '@/apis/dictionaryApis';
import WordCard from '@/components/WordCard';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import { styled } from '@mui/material/styles';

const WordOfTheDayContainer = styled(Paper)(({ theme }) => ({
  padding: theme.spacing(2),
  maxWidth: '100%',
  color: theme.palette.text.secondary,
  backgroundColor: theme.palette.background.paper,
  boxShadow: theme.shadows[3],
  borderRadius: theme.shape.borderRadius,
  marginBottom: theme.spacing(2),
}));

function WordOfTheDay() {
  const { data, isFetching } = useGetWordOfTheDayQuery();

  if (isFetching) return null;
  return (
    <WordOfTheDayContainer>
      <Typography variant="h6" gutterBottom>
        每日一字 (Word of the Day)
      </Typography>
      <WordCard word={data?.data} isHideFavoriteButton isOneWordFullWidth />
    </WordOfTheDayContainer>
  );
}

export default WordOfTheDay;
