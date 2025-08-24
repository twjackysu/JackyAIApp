import { Fab } from '@mui/material';
import ChatIcon from '@mui/icons-material/Chat';
import { Position } from '../types';

interface FloatingButtonProps {
  position: Position;
  isDragging: boolean;
  fabRef: React.RefObject<HTMLButtonElement>;
  onMouseDown: (e: React.MouseEvent) => void;
  onClick: (e: React.MouseEvent) => void;
}

const FloatingButton = ({ 
  position, 
  isDragging, 
  fabRef, 
  onMouseDown, 
  onClick 
}: FloatingButtonProps) => {
  return (
    <Fab
      ref={fabRef}
      color="primary"
      aria-label="chatbot"
      onMouseDown={onMouseDown}
      onClick={onClick}
      sx={{
        position: 'fixed',
        left: position.x,
        top: position.y,
        zIndex: 1300,
        cursor: isDragging ? 'grabbing' : 'grab',
        '&:hover': {
          transform: 'scale(1.1)',
        },
        transition: 'transform 0.2s ease-in-out',
      }}
    >
      <ChatIcon />
    </Fab>
  );
};

export default FloatingButton;