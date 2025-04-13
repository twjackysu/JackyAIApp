import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { orange } from '@mui/material/colors';
import { Link, Route, Routes } from 'react-router-dom';
import { useGetRepositoryWordsQuery } from '../apis/repositoryApis';
import RepositoryNoWordAlert from '../components/RepositoryNoWordAlert';
import ClozeTestCard from './ClozeTestCard';
import TranslationTestCard from './TranslationTestCard';

const styles = {
  button: {
    px: 4,
    py: 2,
    fontSize: '1rem',
    textTransform: 'none',
    boxShadow: 2,
  },
};

function Exam() {
  const { data } = useGetRepositoryWordsQuery({
    pageNumber: 1,
    pageSize: 1,
  });
  const isRepositoryNoWord = !data?.data?.[0];
  return (
    <Box sx={{ p: 2 }}>
      {isRepositoryNoWord ? (
        <RepositoryNoWordAlert />
      ) : (
        <>
          <Box
            sx={{
              display: 'flex',
              justifyContent: 'center',
              gap: 2,
              mb: 3,
            }}
          >
            <Button component={Link} to="cloze" variant="outlined" sx={styles.button}>
              克漏字測驗 (Cloze Test)
            </Button>
            <Button
              component={Link}
              to="translation"
              variant="outlined"
              color="secondary"
              sx={styles.button}
            >
              翻譯測驗 (Translation Test)
            </Button>
            <Button
              component={Link}
              to="sentenceTest"
              variant="outlined"
              sx={{
                ...styles.button,
                color: orange[500],
                borderColor: orange[500],
                ':hover': { borderColor: orange[100] },
              }}
            >
              造句測驗 (Sentence Formation Test)
            </Button>
          </Box>
          <Routes>
            <Route path="cloze" element={<ClozeTestCard />} />
            <Route path="translation" element={<TranslationTestCard />} />
            <Route path="sentenceTest" element={<div>造句測驗還沒做，請選擇其他類型...</div>} />
            <Route path="/" element={<div>請選擇測驗類型。</div>} />
          </Routes>
        </>
      )}
    </Box>
  );
}

export default Exam;
