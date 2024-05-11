import Stack from '@mui/material/Stack';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import FavoriteBorder from '@mui/icons-material/FavoriteBorder';
import { useGetWordQuery } from '@/apis/dictionaryApis';
import { Fragment } from 'react/jsx-runtime';
import { lime, deepPurple, lightGreen, lightBlue } from '@mui/material/colors';
import DividerWithText from '../components/DividerWithText';
import LinearProgress from '@mui/material/LinearProgress';
import IconButton from '@mui/material/IconButton';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import { playVoiceTube, playGoogleNormal } from './utils/audio';

interface Props {
  word?: string | null;
}

function WordCard({ word }: Props) {
  const { data, isFetching, isError } = useGetWordQuery(word!, { skip: !word });
  const handleWordClick = () => {
    if (!word) return;
    playVoiceTube(word);
  };
  return (
    <Card>
      <Stack sx={{ pl: 2 }}>
        <Stack direction="row" justifyContent="space-between">
          <Stack direction="row" spacing={2} alignItems="center">
            <Typography variant="h2" onClick={handleWordClick}>
              {word}
            </Typography>
            <IconButton sx={{ m: 2, p: 2 }} onClick={handleWordClick}>
              <PlayCircleIcon />
            </IconButton>
          </Stack>
          <IconButton sx={{ m: 2, p: 2 }}>
            <FavoriteBorder />
          </IconButton>
        </Stack>
        {isFetching && (
          <Box sx={{ width: '100%' }}>
            <LinearProgress />
          </Box>
        )}
        <Typography color={lightBlue[200]}>{data?.data.kkPhonics}</Typography>
      </Stack>
      <CardContent>
        {isError ? (
          <Typography>Something wrong</Typography>
        ) : (
          <Grid container spacing={2}>
            {data?.data.meanings.map((meaning) => (
              <Grid key={meaning.partOfSpeech} item xs={6}>
                <DividerWithText text="Part of speech" />
                <Typography variant="h6" color={lime[200]}>
                  {meaning.partOfSpeech}
                </Typography>
                <DividerWithText text="Definitions" />
                {meaning.definitions.map((definition) => (
                  <Fragment key={definition.english}>
                    <Typography variant="body1" color={deepPurple[200]}>
                      {definition.english}
                    </Typography>
                    <Typography variant="body1" color={deepPurple[200]}>
                      {definition.chinese}
                    </Typography>
                  </Fragment>
                ))}
                <DividerWithText text="Example sentences" />
                {meaning.exampleSentences.map((exampleSentence) => (
                  <Fragment key={exampleSentence.english}>
                    <Stack direction="row" spacing={2}>
                      <Typography variant="body2" color={lightGreen[200]}>
                        {exampleSentence.english}
                      </Typography>
                      <IconButton
                        sx={{ m: 0, p: 0 }}
                        onClick={() => {
                          playGoogleNormal(exampleSentence.english);
                        }}
                      >
                        <PlayCircleIcon />
                      </IconButton>
                    </Stack>
                    <Typography variant="body2" color={lightGreen[200]}>
                      {exampleSentence.chinese}
                    </Typography>
                  </Fragment>
                ))}
                <DividerWithText text="" />
                <Typography variant="body2">synonyms: {meaning.synonyms.join(', ')}</Typography>
                <Typography variant="body2">antonyms: {meaning.antonyms.join(', ')}</Typography>
                <Typography variant="body2">
                  relatedWords: {meaning.relatedWords.join(', ')}
                </Typography>
              </Grid>
            ))}
          </Grid>
        )}
      </CardContent>
    </Card>
  );
}
export default WordCard;
