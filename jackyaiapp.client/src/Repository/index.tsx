import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import { useGetRepositoryWordsQuery } from '@/apis/repositoryApis';
import LinearProgress from '@mui/material/LinearProgress';
import WordCard from '../components/WordCard';
import List from '@mui/material/List';
import ListItemText from '@mui/material/ListItemText';
import ListItemButton from '@mui/material/ListItemButton';
import { useState, useEffect, useRef, MouseEvent } from 'react';

function Repository() {
  const [pageNumber, setPageNumber] = useState(1);
  const pageSize = 20;
  const [selectedIndex, setSelectedIndex] = useState(0);
  const { data, isFetching } = useGetRepositoryWordsQuery({
    pageNumber,
    pageSize,
  });
  const listRef = useRef<HTMLUListElement>(null);

  const handleListItemClick = (
    _: MouseEvent<HTMLDivElement, globalThis.MouseEvent>,
    index: number,
  ) => {
    setSelectedIndex(index);
  };

  useEffect(() => {
    const handleScroll = () => {
      if (listRef.current) {
        const { scrollTop, scrollHeight, clientHeight } = listRef.current;
        if (
          scrollTop + clientHeight >= scrollHeight &&
          !isFetching &&
          data?.data.length === pageNumber * pageSize
        ) {
          setPageNumber((prevPageNumber) => prevPageNumber + 1);
        }
      }
    };

    const listElement = listRef.current;
    if (listElement) {
      listElement.addEventListener('scroll', handleScroll);
    }

    return () => {
      if (listElement) {
        listElement.removeEventListener('scroll', handleScroll);
      }
    };
  }, [isFetching, data, pageNumber, pageSize]);

  return (
    <Box>
      <Stack direction="row">
        <List
          component="nav"
          ref={listRef}
          sx={{
            maxHeight: '94vh',
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
          {data?.data.map((word, index) => (
            <ListItemButton
              key={index}
              selected={selectedIndex === index}
              onClick={(event) => handleListItemClick(event, index)}
            >
              <ListItemText primary={word.word} />
            </ListItemButton>
          ))}
        </List>
        {isFetching && (
          <Box sx={{ width: '100%' }}>
            <LinearProgress />
          </Box>
        )}
        <WordCard word={data?.data[selectedIndex]} isFavorite />
      </Stack>
    </Box>
  );
}

export default Repository;
