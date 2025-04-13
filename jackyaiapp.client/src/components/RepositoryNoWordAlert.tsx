import Alert from '@mui/material/Alert';

function RepositoryNoWordAlert() {
  return (
    <Alert severity="warning" sx={{ width: '100%' }}>
      The repository currently contains no vocabulary. Please search for words using the dictionary
      and click the heart icon to add them to your repository.
      <br />
      目前儲存庫沒有任何單字, 請使用字典搜尋單字後點擊愛心icon, 把單字加入你的儲存庫
    </Alert>
  );
}

export default RepositoryNoWordAlert;
