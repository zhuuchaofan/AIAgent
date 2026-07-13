export interface CitationNode {
  index: number;
  documentId: string;
  documentName: string;
  chunkIndex: number;
  pageNumber: number;
  sectionTitle: string | null;
  snippetPreview: string;
}

export interface RagChatMessage {
  role: "user" | "assistant";
  content: string;
  citations?: CitationNode[];
  citationIntegrity?: string;
}
