"use client";

import React from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { BookOpen } from "lucide-react";

interface CitationNode {
  index: number;
  documentId: string;
  documentName: string;
  chunkIndex: number;
  pageNumber: number;
  sectionTitle: string | null;
  snippetPreview: string;
}

interface MarkdownProps {
  content: string;
  citations?: CitationNode[];
}

/**
 * Render citation footnote buttons inside text nodes.
 * Uses regex to find [1], [2], ... patterns and replaces them
 * with interactive hover-tooltip buttons.
 */
function parseTextForCitations(
  text: string,
  citations: CitationNode[],
  keyPrefix: string
): React.ReactNode {
  if (!citations || citations.length === 0 || !text) {
    return text;
  }

  const citationRegex = /\[([1-9][0-9]?)\]/g;
  const parts: React.ReactNode[] = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = citationRegex.exec(text)) !== null) {
    const matchIndex = match.index;
    const citationNumber = parseInt(match[1], 10);

    // Text before this citation
    if (matchIndex > lastIndex) {
      parts.push(text.substring(lastIndex, matchIndex));
    }

    // Citation button
    const matchedNode = citations.find((c) => c.index === citationNumber);
    if (matchedNode) {
      parts.push(
        <span
          key={`${keyPrefix}-cite-${matchIndex}`}
          className="relative inline-block group mx-0.5"
        >
          <button
            type="button"
            className="inline-flex items-center justify-center w-5 h-5 text-[10px] font-bold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 rounded-md transition-all align-middle select-none focus:outline-none cursor-help"
          >
            {citationNumber}
          </button>

          {/* Hover Tooltip */}
          <span className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 w-72 p-3 bg-zinc-900 border border-zinc-800 text-zinc-300 rounded-xl opacity-0 pointer-events-none group-hover:opacity-100 group-hover:pointer-events-auto transition-opacity duration-200 shadow-2xl z-50 text-xs text-left font-normal normal-case">
            <span className="flex items-center gap-1.5 font-semibold text-white border-b border-zinc-800 pb-1.5 mb-1.5">
              <BookOpen className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
              <span className="truncate">{matchedNode.documentName}</span>
              <span className="text-[10px] text-zinc-500 font-mono ml-auto">
                Page {matchedNode.pageNumber} | Chunk {matchedNode.chunkIndex}
              </span>
            </span>
            <span className="block text-zinc-400 leading-normal line-clamp-4 italic">
              &ldquo;{matchedNode.snippetPreview}&rdquo;
            </span>
            {/* Arrow */}
            <span className="absolute top-full left-1/2 -translate-x-1/2 -mt-1 border-4 border-transparent border-t-zinc-900" />
          </span>
        </span>
      );
    } else {
      // Unmatched footnote → render as raw text
      parts.push(match[0]);
    }

    lastIndex = citationRegex.lastIndex;
  }

  // Remaining text after last citation
  if (lastIndex < text.length) {
    parts.push(text.substring(lastIndex));
  }

  return parts.length > 0 ? <>{parts}</> : text;
}

/**
 * Recursively walk React children to find text nodes and inject citation buttons.
 */
function renderChildrenWithCitations(
  children: React.ReactNode,
  citations: CitationNode[],
  counter: { current: number }
): React.ReactNode {
  if (typeof children === "string") {
    return parseTextForCitations(children, citations, `rc-${counter.current++}`);
  }
  if (Array.isArray(children)) {
    return children.map((child) => {
      if (typeof child === "string") {
        return (
          <React.Fragment key={`rf-${counter.current}`}>
            {parseTextForCitations(child, citations, `rc-${counter.current++}`)}
          </React.Fragment>
        );
      }
      if (React.isValidElement(child)) {
        const childProps = child.props as Record<string, unknown> & { children?: React.ReactNode };
        if (childProps.children) {
          const newProps: Record<string, unknown> = { ...childProps };
          newProps.children = renderChildrenWithCitations(childProps.children, citations, counter);
          return React.cloneElement(child, newProps);
        }
      }
      return child;
    });
  }
  if (React.isValidElement(children)) {
    const childProps = children.props as Record<string, unknown> & { children?: React.ReactNode };
    if (childProps.children) {
      const newProps: Record<string, unknown> = { ...childProps };
      newProps.children = renderChildrenWithCitations(childProps.children, citations, counter);
      return React.cloneElement(children, newProps);
    }
  }
  return children;
}

export function Markdown({ content, citations = [] }: MarkdownProps) {
  const citationCounter = { current: 0 };

  return (
    <div className="text-zinc-200 text-sm min-w-0 overflow-hidden">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          // ---- Block-level elements ----
          p: ({ children }) => (
            <p className="mb-2 last:mb-0 leading-relaxed break-words [overflow-wrap:anywhere]">
              {renderChildrenWithCitations(children, citations, citationCounter)}
            </p>
          ),
          h1: ({ children }) => (
            <h1 className="text-lg font-bold text-white mt-4 mb-2 border-b border-zinc-800 pb-1 break-words">
              {children}
            </h1>
          ),
          h2: ({ children }) => (
            <h2 className="text-base font-semibold text-white mt-3 mb-1.5 break-words">
              {children}
            </h2>
          ),
          h3: ({ children }) => (
            <h3 className="text-sm font-semibold text-white mt-2.5 mb-1 break-words">
              {children}
            </h3>
          ),
          h4: ({ children }) => (
            <h4 className="text-sm font-medium text-zinc-100 mt-2 mb-1 break-words">
              {children}
            </h4>
          ),
          h5: ({ children }) => (
            <h5 className="text-xs font-medium text-zinc-100 mt-2 mb-1 break-words">
              {children}
            </h5>
          ),
          h6: ({ children }) => (
            <h6 className="text-xs font-medium text-zinc-200 mt-2 mb-1 break-words">
              {children}
            </h6>
          ),
          ul: ({ children }) => (
            <ul className="list-disc pl-5 mb-3 space-y-1">
              {children}
            </ul>
          ),
          ol: ({ children }) => (
            <ol className="list-decimal pl-5 mb-3 space-y-1">
              {children}
            </ol>
          ),
          li: ({ children }) => (
            <li className="leading-relaxed">
              {renderChildrenWithCitations(children, citations, citationCounter)}
            </li>
          ),
          blockquote: ({ children }) => (
            <blockquote className="border-l-4 border-indigo-500 bg-indigo-500/5 pl-4 py-1.5 my-3 rounded-r italic text-zinc-400 min-w-0 break-words">
              {children}
            </blockquote>
          ),
          hr: () => <hr className="my-4 border-zinc-800" />,

          // ---- Inline elements ----
          strong: ({ children }) => (
            <strong className="font-bold text-white">
              {children}
            </strong>
          ),
          em: ({ children }) => (
            <em className="italic text-zinc-300">{children}</em>
          ),
          del: ({ children }) => (
            <del className="line-through text-zinc-500">{children}</del>
          ),

          // ---- Code ----
          pre: ({ children }) => (
            <pre className="bg-zinc-950 border border-zinc-800/80 rounded-xl p-3.5 my-3 overflow-x-auto text-xs font-mono text-zinc-300 shadow-inner max-w-full">
              {children}
            </pre>
          ),
          code: ({ className, children, ...props }) => {
            // Inline code: no className and no newlines in content
            const text = String(children);
            const isInline = !className && !text.includes("\n");
            if (isInline) {
              return (
                <code
                  className="bg-zinc-800/60 text-indigo-300 border border-zinc-700/30 px-1.5 py-0.5 rounded text-xs font-mono"
                  {...props}
                >
                  {children}
                </code>
              );
            }
            return (
              <code className={`${className || ""} font-mono block text-xs`} {...props}>
                {children}
              </code>
            );
          },

          // ---- Table ----
          table: ({ children }) => (
            <div className="overflow-x-auto my-3 border border-zinc-800 rounded-xl max-w-full">
              <table className="min-w-full text-left border-collapse text-xs">
                {children}
              </table>
            </div>
          ),
          thead: ({ children }) => (
            <thead className="bg-zinc-900 border-b border-zinc-800 text-zinc-200 uppercase tracking-wider font-semibold">
              {children}
            </thead>
          ),
          tbody: ({ children }) => (
            <tbody className="divide-y divide-zinc-800/50 bg-zinc-900/10">
              {children}
            </tbody>
          ),
          tr: ({ children }) => (
            <tr className="hover:bg-zinc-900/30 transition-colors">{children}</tr>
          ),
          th: ({ children }) => (
            <th className="px-4 py-2.5 font-semibold">{children}</th>
          ),
          td: ({ children }) => (
            <td className="px-4 py-2.5 text-zinc-300 leading-normal">{children}</td>
          ),

          // ---- Link (safe: react-markdown auto-adds rel="nofollow noopener noreferrer" by default, but we enforce target) ----
          a: ({ href, children }) => (
            <a
              href={href}
              target="_blank"
              rel="noopener noreferrer"
              className="text-indigo-400 hover:text-indigo-300 underline underline-offset-2 transition-colors break-all"
            >
              {children}
            </a>
          ),

          // ---- Images (safe: react-markdown does NOT allow arbitrary HTML) ----
          img: ({ src, alt }) => (
            <span className="block my-2 text-zinc-500 text-xs italic">
              [Image: {alt || String(src || "")}]
            </span>
          ),
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  );
}
