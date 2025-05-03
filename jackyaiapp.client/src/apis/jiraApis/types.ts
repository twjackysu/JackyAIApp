export interface JiraIssue {
  key: string;
  fields: Fields;
}

export interface Fields {
  summary: string;
  description: string;
  labels: string[];
}

export interface JiraSearchResponse {
  issues: JiraIssue[];
}

export interface JiraSearchRequest {
  body: {
    jiraConfigId: string;
    jql: string;
  };
}

interface JiraConfigBase {
  domain: string;
  email: string;
  token: string;
}

export interface JiraConfigRequest {
  body: JiraConfigBase;
}

export interface JiraConfig extends JiraConfigBase {
  id: string;
}
