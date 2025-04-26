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
    domain: string;
    email: string;
    token: string;
    jql: string;
  };
}
