import DeleteForeverIcon from '@mui/icons-material/DeleteForever';
import IconButton from '@mui/material/IconButton';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useInvalidWordMutation } from '@/apis/dictionaryApis';
import { ActionTooltip } from '@/components/hints';

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
  if (!data?.data.isAdmin) {
    return null;
  }
  return (
    // The handler belongs on the button, not the icon: a click landing on the button's own
    // padding used to do nothing, and keyboard activation fires the click on the <button>
    // element — which never reached a handler sitting on the child <svg>.
    <ActionTooltip title="標記為無效單字">
      <IconButton sx={{ m: 2, p: 2 }} onClick={handleInvalidClick}>
        <DeleteForeverIcon />
      </IconButton>
    </ActionTooltip>
  );
}

export default AdminInvalidWordButton;
