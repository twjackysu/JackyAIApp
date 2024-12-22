import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { Link, Route, Routes } from 'react-router-dom';
import ClozeTestCard from './ClozeTestCard';
import SentenceFormationTestCard from './SentenceFormationTestCard';

function Exam() {
  return (
    <Box sx={{ p: 2 }}>
      <Routes>
        <Route path="cloze" element={<ClozeTestCard />} />
        <Route path="sentenceTest" element={<SentenceFormationTestCard />} />
        <Route path="/" element={<div>請選擇測驗類型。</div>} />
      </Routes>
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'center',
          gap: 2,
          mb: 3,
        }}
      >
        <Button
          component={Link}
          to="cloze"
          variant="outlined"
          sx={{
            px: 4,
            py: 2,
            fontSize: '1rem',
            textTransform: 'none',
            boxShadow: 3,
          }}
        >
          克漏字測驗 (Cloze Test)
        </Button>
        <Button
          component={Link}
          to="sentenceTest"
          variant="outlined"
          color="secondary"
          sx={{
            px: 4,
            py: 2,
            fontSize: '1rem',
            textTransform: 'none',
            boxShadow: 3,
          }}
        >
          造句測驗 (Sentence Formation Test)
        </Button>
      </Box>
    </Box>
  );
}

export default Exam;
