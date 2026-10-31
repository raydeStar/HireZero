import { api } from "../lib/api";

export type ConversationAttachment = { name: string; text: string; kind: "document" | "image-ocr" };
export const attachmentAccept = ".txt,.md,.docx,.pdf,.png,.jpg,.jpeg";
export const attachmentBytes = (files: ConversationAttachment[]) =>
  files.reduce((total, file) => total + new TextEncoder().encode(file.text.trim()).length, 0);

export async function readConversationAttachment(file: File): Promise<ConversationAttachment> {
  const extension = file.name.split(".").pop()?.toLowerCase() ?? "";
  if (!attachmentAccept.split(",").includes("." + extension))
    throw new Error("Choose a screenshot (PNG or JPEG), PDF, Word .docx, text or Markdown file.");
  const plain = extension === "txt" || extension === "md";
  if (file.size > (plain ? 65536 : 2 * 1024 * 1024))
    throw new Error(plain ? "Choose a text file up to 64 KiB." : "Choose a file up to 2 MiB.");
  const image = ["png", "jpg", "jpeg"].includes(extension);
  const buffer = await file.arrayBuffer();
  let text: string;
  if (plain) {
    try {
      text = new TextDecoder("utf-8", { fatal: true }).decode(buffer);
    } catch {
      throw new Error("Export this text file as UTF-8 and try again.");
    }
  } else {
    let binary = "";
    const bytes = new Uint8Array(buffer);
    for (let index = 0; index < bytes.length; index += 16384)
      binary += String.fromCharCode(...bytes.subarray(index, index + 16384));
    const preview = await api<{ text: string }>(
      "/company/documents/extract-" + (image ? "image" : extension === "pdf" ? "pdf" : "word"),
      "POST",
      { name: file.name, contentBase64: btoa(binary) },
      { timeoutMs: 20000 },
    );
    text = preview.text;
  }
  if (!text.trim() || /\u0000/.test(text))
    throw new Error("No usable text found. Try another file or paste the relevant text.");
  return { name: file.name, text, kind: image ? "image-ocr" : "document" };
}
