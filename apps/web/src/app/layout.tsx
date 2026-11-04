import type { Metadata } from "next";
import "./globals.css";
import { AppModeProvider } from "@/lib/context/app-mode-context";
import { AuthProvider } from "@/lib/context/auth-context";
import { AppShell } from "@/components/layout/shell";

export const metadata: Metadata = {
  title: "OrionSearch — Архиви за автономни проучвания",
  description: "Работно пространство за автономни проучвания и архивиране на знания.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="bg">
      <body className="bg-workspace text-content-primary antialiased">
        <AppModeProvider>
          <AuthProvider><AppShell>{children}</AppShell></AuthProvider>
        </AppModeProvider>
      </body>
    </html>
  );
}
