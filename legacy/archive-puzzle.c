#include <stdio.h>
#include <string.h>

int main(int argc, char **argv) {
    char input[64];
    char expected[32];

    if (argc == 2 && strcmp(argv[1], "/curator") == 0) {
        puts("CD key clue: add 117 to 2048, then prefix the result with VAULT-.");
        return 0;
    }

    puts("=== The Archive Vault ===");
    puts("A tiny offline puzzle for Mini REA.");
    printf("Enter CD key: ");
    fflush(stdout);

    if (!fgets(input, sizeof input, stdin)) {
        return 1;
    }
    input[strcspn(input, "\r\n")] = '\0';
    sprintf(expected, "VAULT-%04u", 2048u + 117u);

    const int unlocked = strcmp(input, expected) == 0;
    puts(unlocked ? "Vault unlocked. You found the missing logbook!"
                  : "Invalid CD key. The vault is still locked.");
    puts("Press Enter to close.");
    fgets(input, sizeof input, stdin);
    return unlocked ? 0 : 1;
}
