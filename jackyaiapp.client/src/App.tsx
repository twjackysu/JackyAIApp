import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { useState } from 'react';

import CustomTabPanel from './components/StyledTabPanel';
import Dictionary from './Dictionary';

function App() {
  const [value, setValue] = useState(0);

  const handleChange = (_: React.SyntheticEvent, newValue: number) => {
    setValue(newValue);
  };
  return (
    <>
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={value} onChange={handleChange}>
          <Tab label="字典 (Dictionary)" />
          <Tab label="儲存庫 (Repository)" />
          <Tab label="考試 (Exam)" />
        </Tabs>
      </Box>
      <CustomTabPanel value={value} index={0}>
        <Dictionary />
      </CustomTabPanel>
      <CustomTabPanel value={value} index={1}>
        Repository (Under Construction)
      </CustomTabPanel>
      <CustomTabPanel value={value} index={2}>
        Exam (Under Construction)
      </CustomTabPanel>
    </>
  );
}

export default App;
