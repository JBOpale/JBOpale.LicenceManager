// Utilitaire client « Code poste » : affiche le code de ce poste (a transmettre pour obtenir une
// licence liee a ce poste) et le copie dans le presse-papiers. Meme calcul que le validateur.
#include "LicenceValidator.h"

#ifndef NOMINMAX
#define NOMINMAX // windows.h definirait sinon des macros min/max qui cassent std::min
#endif
#include <windows.h>

#include <cstring>
#include <string>

namespace {

bool CopyToClipboard(const std::wstring& text) {
    if (!OpenClipboard(nullptr)) return false;
    EmptyClipboard();
    size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
    bool ok = false;
    if (memory) {
        void* target = GlobalLock(memory);
        if (target) {
            std::memcpy(target, text.c_str(), bytes);
            GlobalUnlock(memory);
            ok = SetClipboardData(CF_UNICODETEXT, memory) != nullptr;
        }
        if (!ok) GlobalFree(memory); // sinon, appartient desormais au presse-papiers
    }
    CloseClipboard();
    return ok;
}

} // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    const wchar_t* title = L"Code poste";
    std::string code = licmgr::LicenceValidator::GetMachineCode();
    if (code.empty()) {
        MessageBoxW(nullptr, L"Impossible d'identifier ce poste.\n\nContactez le support JB-Opale.",
                    title, MB_OK | MB_ICONERROR);
        return 1;
    }

    std::wstring wcode(code.begin(), code.end()); // code en ASCII
    bool copied = CopyToClipboard(wcode);
    std::wstring message = L"Code de ce poste :\n\n        " + wcode + L"\n\n";
    if (copied) message += L"Il a été copié dans le presse-papiers.\n";
    message += L"Transmettez-le à JB-Opale pour recevoir votre clé de licence.";
    MessageBoxW(nullptr, message.c_str(), title, MB_OK | MB_ICONINFORMATION);
    return 0;
}
