import FavoriteIcon from '@mui/icons-material/Favorite';
import FavoriteBorder from '@mui/icons-material/FavoriteBorder';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import { deepPurple, lightBlue, lightGreen, lime } from '@mui/material/colors';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { Fragment } from 'react/jsx-runtime';

import { Word } from '../apis/dictionaryApis/types';
import { playGoogleNormal, playVoiceTube } from '../Dictionary/utils/audio';

import AdminInvalidWordButton from './AdminInvalidWordButton';
import AILoading from './AILoading';
import DividerWithText from './DividerWithText';
import FetchBaseQueryErrorMessage from './FetchBaseQueryErrorMessage';
import WordLinkList from './WordLinkList';

import {
  useDeleteRepositoryWordMutation,
  usePutRepositoryWordMutation,
} from '@/apis/repositoryApis';
import { ActionTooltip } from '@/components/hints';

interface Props {
  word?: Word | null;
  isFetching?: boolean;
  isError?: boolean;
  isFavorite?: boolean;
  error?: FetchBaseQueryError | SerializedError;
  isOneWordFullWidth?: boolean;
  isHideFavoriteButton?: boolean;
}

function WordCard({
  word,
  isFetching,
  isError,
  isFavorite,
  error,
  isOneWordFullWidth,
  isHideFavoriteButton,
}: Props) {
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
        <Stack
          direction="row"
          sx={{
            flexWrap: 'wrap',
            rowGap: 1,
            justifyContent: 'space-between',
          }}
        >
          <Stack
            direction="row"
            spacing={{ xs: 1, sm: 2 }}
            sx={{
              alignItems: 'center',
              minWidth: 0,
            }}
          >
            <Typography
              variant="h2"
              sx={{
                fontSize: { xs: '1.75rem', sm: '2.5rem', md: '3.75rem' },
                wordBreak: 'break-word',
              }}
            >
              {word?.word}
            </Typography>
            {word && (
              <ActionTooltip title="播放發音">
                <IconButton
                  sx={{ m: { xs: 0, sm: 2 }, p: { xs: 0.5, sm: 2 } }}
                  onClick={handleWordClick}
                >
                  <PlayCircleIcon />
                </IconButton>
              </ActionTooltip>
            )}
          </Stack>
          {word && !isHideFavoriteButton && (
            <Stack
              direction="row"
              spacing={{ xs: 1, sm: 2 }}
              sx={{
                alignItems: 'center',
              }}
            >
              {/* The handler belongs on the button, not the icon: a click landing on the
                  button's own padding used to do nothing, and keyboard activation fires the
                  click on the <button> element — which never reached a handler sitting on
                  the child <svg>.
                  Named with a bare aria-label rather than an ActionTooltip because this button
                  goes disabled while fetching, and a disabled child fires no events for a
                  Tooltip to listen to (MUI logs a warning for exactly this). */}
              <IconButton
                sx={{ m: { xs: 0, sm: 2 }, p: { xs: 0.5, sm: 2 } }}
                disabled={isFetching}
                aria-label={isFavorite ? '從單字庫移除' : '加入單字庫'}
                onClick={isFavorite ? handleDeleteFavoriteClick : handleFavoriteClick}
              >
                {isFavorite ? <FavoriteIcon /> : <FavoriteBorder />}
              </IconButton>
              <AdminInvalidWordButton word={word.word} />
            </Stack>
          )}
        </Stack>
        {isFetching && <AILoading />}
        <Typography sx={{ color: lightBlue[200] }}>{word?.kkPhonics}</Typography>
      </Stack>
      <CardContent>
        {isError ? (
          <FetchBaseQueryErrorMessage error={error} />
        ) : (
          <Grid container spacing={2}>
            {word?.meanings.map((meaning) => (
              <Grid
                key={meaning.partOfSpeech}
                size={isOneWordFullWidth || word?.meanings.length === 1 ? 12 : 6}
              >
                <DividerWithText text="Part of speech" />
                <Typography variant="h6" sx={{ color: lime[200] }}>
                  {meaning.partOfSpeech}
                </Typography>
                <DividerWithText text="Definitions" />
                {meaning.definitions.map((definition) => (
                  <Fragment key={definition.english}>
                    <Typography variant="body1" sx={{ color: deepPurple[200] }}>
                      {definition.english}
                    </Typography>
                    <Typography variant="body1" sx={{ color: deepPurple[200] }}>
                      {definition.chinese}
                    </Typography>
                  </Fragment>
                ))}
                <DividerWithText text="Example sentences" />
                {meaning.exampleSentences.map((exampleSentence) => (
                  <Fragment key={exampleSentence.english}>
                    <Stack direction="row" spacing={2}>
                      <Typography variant="body2" sx={{ color: lightGreen[200] }}>
                        {exampleSentence.english}
                      </Typography>
                      <ActionTooltip title="播放例句">
                        <IconButton
                          sx={{ m: 0, p: 0 }}
                          onClick={() => {
                            playGoogleNormal(exampleSentence.english);
                          }}
                        >
                          <PlayCircleIcon />
                        </IconButton>
                      </ActionTooltip>
                    </Stack>
                    <Typography variant="body2" sx={{ color: lightGreen[200] }}>
                      {exampleSentence.chinese}
                    </Typography>
                  </Fragment>
                ))}
                <DividerWithText text="" />
                <WordLinkList label="synonyms" words={meaning.synonyms} />
                <WordLinkList label="antonyms" words={meaning.antonyms} />
                <WordLinkList label="relatedWords" words={meaning.relatedWords} />
              </Grid>
            ))}
          </Grid>
        )}
      </CardContent>
    </Card>
  );
}
export default WordCard;
