import Stack from '@mui/material/Stack';
import Grid from '@mui/material/Grid';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import FavoriteBorder from '@mui/icons-material/FavoriteBorder';
import FavoriteIcon from '@mui/icons-material/Favorite';
import { Fragment } from 'react/jsx-runtime';
import { lime, deepPurple, lightGreen, lightBlue } from '@mui/material/colors';
import DividerWithText from './DividerWithText';
import IconButton from '@mui/material/IconButton';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import { playVoiceTube, playGoogleNormal } from '../Dictionary/utils/audio';
import {
  usePutRepositoryWordMutation,
  useDeleteRepositoryWordMutation,
} from '@/apis/repositoryApis';
import { Word } from '../apis/dictionaryApis/types';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { SerializedError } from '@reduxjs/toolkit';
import FetchBaseQueryErrorMessage from './FetchBaseQueryErrorMessage';
import AILoading from './AILoading';
import AdminInvalidWordButton from './AdminInvalidWordButton';

interface Props {
  word?: Word | null;
  isFetching?: boolean;
  isError?: boolean;
  isFavorite?: boolean;
  error?: FetchBaseQueryError | SerializedError;
}

function WordCard({ word, isFetching, isError, isFavorite, error }: Props) {
  const [putRepositoryWordMutation] = usePutRepositoryWordMutation();
  const [deleteRepositoryWordMutation] = useDeleteRepositoryWordMutation();
  const handleWordClick = () => {
    if (!word) return;
    playVoiceTube(word.word);
  };
  const handleFavoriteClick = () => {
    const wordId = word?.id;
    if (!wordId) return;
    putRepositoryWordMutation(wordId);
  };
  const handleDeleteFavoriteClick = () => {
    const wordId = word?.id;
    if (!wordId) return;
    deleteRepositoryWordMutation(wordId);
  };

  return (
    <Card sx={{ width: '100%' }}>
      <Stack sx={{ pl: 2 }}>
        <Stack direction="row" justifyContent="space-between">
          <Stack direction="row" spacing={2} alignItems="center">
            <Typography variant="h2">{word?.word}</Typography>
            {word && (
              <IconButton sx={{ m: 2, p: 2 }} onClick={handleWordClick}>
                <PlayCircleIcon />
              </IconButton>
            )}
          </Stack>
          {word && (
            <Stack direction="row" spacing={2} alignItems="center">
              <IconButton sx={{ m: 2, p: 2 }}>
                {isFavorite ? (
                  <FavoriteIcon onClick={handleDeleteFavoriteClick} />
                ) : (
                  <FavoriteBorder onClick={handleFavoriteClick} />
                )}
              </IconButton>
              <AdminInvalidWordButton word={word.word} />
            </Stack>
          )}
        </Stack>
        {isFetching && <AILoading />}
        <Typography color={lightBlue[200]}>{word?.kkPhonics}</Typography>
      </Stack>
      <CardContent>
        {isError ? (
          <FetchBaseQueryErrorMessage error={error} />
        ) : (
          <Grid container spacing={2}>
            {word?.meanings.map((meaning) => (
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
