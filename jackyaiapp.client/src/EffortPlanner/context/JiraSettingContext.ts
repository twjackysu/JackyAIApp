import { createContext } from 'react';

interface JiraSettingContextProps {
  jiraDomain: string;
  setJiraDomain: React.Dispatch<React.SetStateAction<string>>;
  jiraEmail: string;
  setJiraEmail: React.Dispatch<React.SetStateAction<string>>;
  jiraToken: string;
  setJiraToken: React.Dispatch<React.SetStateAction<string>>;
}

const JiraSettingContext = createContext<JiraSettingContextProps>({
  jiraDomain: '',
  setJiraDomain: () => {},
  jiraEmail: '',
  setJiraEmail: () => {},
  jiraToken: '',
  setJiraToken: () => {},
});

export default JiraSettingContext;
