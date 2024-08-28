import { useGetUserInfoQuery } from '@/apis/accountApis';
import DeleteForeverIcon from '@mui/icons-material/DeleteForever';
import IconButton from '@mui/material/IconButton';
import { useInvalidWordMutation } from '@/apis/dictionaryApis';

interface Props {
  word?: string | null;
}

function AdminInvalidWordButton({ word }: Props) {
  const [invalidWordMutation] = useInvalidWordMutation();
  const { data } = useGetUserInfoQuery();
  const handleInvalidClick = () => {
    if (word) {
      invalidWordMutation(word);
    }
  };
  if (!data?.isAdmin) {
    return null;
  }
  return (
    <IconButton sx={{ m: 2, p: 2 }}>
      <DeleteForeverIcon onClick={handleInvalidClick} />
    </IconButton>
  );
}

export default AdminInvalidWordButton;
