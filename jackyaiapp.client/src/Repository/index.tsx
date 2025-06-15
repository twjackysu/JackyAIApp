import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';

import { useGetRepositoryWordsQuery } from '@/apis/repositoryApis';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import { MouseEvent, useEffect, useRef, useState } from 'react';
import { Word } from '../apis/dictionaryApis/types';
import RepositoryNoWordAlert from '../components/RepositoryNoWordAlert';
import WordCard from '../components/WordCard';

function Repository() {
  const [pageNumber, setPageNumber] = useState(1);
  const pageSize = 20;
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [accumulatedData, setAccumulatedData] = useState<Record<number, Word[]>>({});
  const { data, isFetching } = useGetRepositoryWordsQuery({
    pageNumber,
    pageSize,
  });
  const listRef = useRef<HTMLUListElement>(null);
  const isFetchingRef = useRef(isFetching);
  const hasMoreRef = useRef(true);

  // 更新 refs 以便在事件處理器中使用最新值
  isFetchingRef.current = isFetching;

  const handleListItemClick = (
    _: MouseEvent<HTMLDivElement, globalThis.MouseEvent>,
    index: number,
  ) => {
    setSelectedIndex(index);
  };

  useEffect(() => {
    if (!isFetching && data) {
      setAccumulatedData((prev) => {
        return {
          ...prev,
          [pageNumber]: data.data ?? [],
        };
      });

      if (data.data.length < pageSize) {
        hasMoreRef.current = false;
      }
    }
  }, [isFetching, data, pageNumber, pageSize]);

  const allWords = Object.values(accumulatedData).reduce((acc, words) => acc.concat(words), []);

  useEffect(() => {
    const handleScroll = () => {
      if (listRef.current && hasMoreRef.current && !isFetchingRef.current) {
        const { scrollTop, scrollHeight, clientHeight } = listRef.current;
        // 在接近底部時觸發載入 (留 10px 的緩衝)
        if (scrollTop + clientHeight >= scrollHeight - 10) {
          setPageNumber((prevPageNumber) => prevPageNumber + 1);
        }
      }
    };

    const listElement = listRef.current;
    if (listElement) {
      listElement.addEventListener('scroll', handleScroll);

      return () => {
        listElement.removeEventListener('scroll', handleScroll);
      };
    }
  }, [allWords.length]); // 依賴 allWords.length，確保當數據變化時重新設置監聽器

  return (
    <Box>
      <Stack direction="row">
        {allWords.length === 0 ? (
          <Box p={2}>
            <RepositoryNoWordAlert />
          </Box>
        ) : (
          <>
            <List
              component="nav"
              ref={listRef}
              sx={{
                maxHeight: '80vh',
                overflowY: 'auto',
                overflowX: 'hidden',
                scrollbarWidth: 'thin', // For Firefox
                '&::-webkit-scrollbar': {
                  width: '8px',
                },
                '&::-webkit-scrollbar-thumb': {
                  backgroundColor: 'rgba(0, 0, 0, 0.3)',
                  borderRadius: '10px',
                },
                '&::-webkit-scrollbar-thumb:hover': {
                  backgroundColor: 'rgba(0, 0, 0, 0.5)',
                },
                '&::-webkit-scrollbar-track': {
                  backgroundColor: 'transparent',
                },
              }}
            >
              {allWords.map((word, index) => (
                <ListItemButton
                  key={index}
                  selected={selectedIndex === index}
                  onClick={(event) => handleListItemClick(event, index)}
                >
                  <ListItemText primary={word.word} />
                </ListItemButton>
              ))}
            </List>
            <WordCard word={allWords[selectedIndex]} isFavorite isFetching={isFetching} />
          </>
        )}
      </Stack>
    </Box>
  );
}

export default Repository;
