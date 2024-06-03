import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import { useGetWordsQuery } from '@/apis/repositoryApis';
import LinearProgress from '@mui/material/LinearProgress';
import WordCard from '../components/WordCard';

function Repository() {
  const { data, isFetching } = useGetWordsQuery();
  return (
    <Box>
      <Stack>
        {isFetching && (
          <Box sx={{ width: '100%' }}>
            <LinearProgress />
          </Box>
        )}
        {data?.data.map((word) => <WordCard word={word} />)}
      </Stack>
    </Box>
  );
}

export default Repository;
