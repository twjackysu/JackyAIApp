import { Link } from '@mui/material';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { forwardRef, useImperativeHandle } from 'react';

const recentlySearchedKey = 'recentlySearched';
interface RecentlySearchedProps {
  onClickRecentlySearchedText: (text: string) => void;
}

export interface RecentlySearchedRef {
  handleEnterKeyDown: (
    e: React.KeyboardEvent<HTMLDivElement>,
    word: string | null | undefined,
  ) => void;
}

const RecentlySearched = forwardRef<RecentlySearchedRef, RecentlySearchedProps>(
  ({ onClickRecentlySearchedText }, ref) => {
    const updateRecentlySearched = (newWord: string) => {
      const savedRecentlySearched = localStorage.getItem(recentlySearchedKey);
      let recentlySearchedTexts = savedRecentlySearched ? JSON.parse(savedRecentlySearched) : [];

      if (!recentlySearchedTexts.includes(newWord)) {
        recentlySearchedTexts = [newWord, ...recentlySearchedTexts].slice(0, 10);
        localStorage.setItem(recentlySearchedKey, JSON.stringify(recentlySearchedTexts));
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

    let recentWords: string[] = [];
    const savedWords = localStorage.getItem(recentlySearchedKey);
    if (savedWords) {
      try {
        const parsedWords = JSON.parse(savedWords);
        if (Array.isArray(parsedWords) && parsedWords.every((item) => typeof item === 'string')) {
          recentWords = parsedWords;
        }
      } catch (e) {
        console.error('Failed to parse recent searched text from localStorage', e);
      }
    }

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
      <Box width="50%" sx={{ p: 2 }}>
        <Typography variant="h6" gutterBottom>
          最近搜尋 (Recently searched)
        </Typography>
        <Typography variant="body1">
          {recentWords.map((text, i) => (
            <>
              <Link href="#" onClick={(e) => handleClickLinkText(e, text)}>
                {text}
              </Link>
              {i !== recentWords.length - 1 && ', '}
            </>
          ))}
        </Typography>
      </Box>
    );
  },
);

export default RecentlySearched;
