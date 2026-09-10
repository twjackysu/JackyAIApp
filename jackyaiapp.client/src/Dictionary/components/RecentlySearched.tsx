import { Link } from '@mui/material';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { Fragment, useImperativeHandle } from 'react';

import { readJson, writeJson } from '@/utils/storage';

const recentlySearchedKey = 'recentlySearched';

/** The stored list, or an empty one when the entry is missing or is no longer an array of
 * strings. Both the render and the update path read through here, so an entry left behind by
 * an older version can't reach either. */
function readRecentWords(): string[] {
  const parsed = readJson<unknown>(recentlySearchedKey);
  return Array.isArray(parsed) && parsed.every((item) => typeof item === 'string')
    ? (parsed as string[])
    : [];
}

interface RecentlySearchedProps {
  onClickRecentlySearchedText: (text: string) => void;
  ref?: React.Ref<RecentlySearchedRef>;
}

export interface RecentlySearchedRef {
  handleEnterKeyDown: (
    e: React.KeyboardEvent<HTMLDivElement>,
    word: string | null | undefined,
  ) => void;
}

function RecentlySearched({ onClickRecentlySearchedText, ref }: RecentlySearchedProps) {
  const updateRecentlySearched = (newWord: string) => {
    let recentlySearchedTexts = readRecentWords();

    if (!recentlySearchedTexts.includes(newWord)) {
      recentlySearchedTexts = [newWord, ...recentlySearchedTexts].slice(0, 10);
      writeJson(recentlySearchedKey, recentlySearchedTexts);
    }
  };
  const handleEnterKeyDown = (
    _: React.KeyboardEvent<HTMLDivElement>,
    word: string | null | undefined,
  ) => {
    if (word) {
      updateRecentlySearched(word);
    }
  };
  useImperativeHandle(ref, () => ({
    handleEnterKeyDown,
  }));

  const recentWords = readRecentWords();

  const handleClickLinkText = (
    e: React.MouseEvent<HTMLAnchorElement, MouseEvent>,
    text: string,
  ) => {
    e.preventDefault();
    onClickRecentlySearchedText(text);
  };
  if (recentWords.length === 0) {
    return null;
  }
  return (
    <Box
      sx={{
        width: '50%',
        p: 2,
      }}
    >
      <Typography variant="h6" gutterBottom>
        最近搜尋 (Recently searched)
      </Typography>
      <Typography variant="body1">
        {recentWords.map((text, i) => (
          <Fragment key={text}>
            <Link href="#" onClick={(e) => handleClickLinkText(e, text)}>
              {text}
            </Link>
            {i !== recentWords.length - 1 && ', '}
          </Fragment>
        ))}
      </Typography>
    </Box>
  );
}

export default RecentlySearched;
