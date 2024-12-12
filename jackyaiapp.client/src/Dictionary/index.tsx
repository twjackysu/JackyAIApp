import Box from '@mui/material/Box';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import WordCard from '../components/WordCard';
import { useGetWordQuery } from '@/apis/dictionaryApis';
import { useGetRepositoryWordsByWordIdQuery } from '../apis/repositoryApis';
import DailyWord from './components/DailyWord'; // 引入 DailyWord 組件

function Dictionary() {
  const [text, setText] = useState<string>('');
  const [word, setWord] = useState<string | null>(null);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter') {
      setWord(text);
    }
  };

  const { data, isFetching, isError, error } = useGetWordQuery(word!, { skip: !word });
  const wordId = data?.data.id ?? '';
  const { data: personalWordsData, isFetching: personalWordsIsFetching } =
    useGetRepositoryWordsByWordIdQuery(wordId, { skip: !wordId });

  return (
    <Box
      display="flex"
      flexDirection="column"
      justifyContent={word ? 'flex-start' : 'center'}
      alignItems="center"
      height="calc(100vh - 48px)"
      padding={word ? '20px' : '0'}
    >
      <TextField
        label="Search"
        variant="outlined"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={handleKeyDown}
        sx={{
          width: '50%',
          marginBottom: word ? '20px' : '0',
          transition: 'margin-bottom 0.3s ease',
        }}
      />
      {!word && <DailyWord />} {/* 當沒有搜索詞語時顯示每日一詞 */}
      {word && (
        <Box width={data?.data.meanings.length == 1 ? '80%' : '100%'}>
          <WordCard
            word={data?.data}
            isFetching={isFetching || personalWordsIsFetching}
            isError={isError}
            isFavorite={!!personalWordsData?.data}
            error={error}
          />
        </Box>
      )}
    </Box>
  );
}

export default Dictionary;
