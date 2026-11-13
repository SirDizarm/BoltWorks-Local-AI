/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#include <stdio.h>

int c_to_f(int celsius);

int main(void) {
    int celsius;
    if (scanf("%d", &celsius) != 1 || celsius < -100 || celsius > 100) {
        fputs("Enter an integer from -100 to 100.\n", stderr);
        return 1;
    }
    printf("%d\n", c_to_f(celsius));
    return 0;
}
