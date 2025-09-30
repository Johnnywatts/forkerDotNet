## Simplified approach

# Fundamentals
1. Input direcotry with files being dropped in constantly by a digital pathology scanner
2. 2 copies of each file in input direcotry must be made to:
    a. primary Clinical folder which is read from constantly by NPIC ingestion workflow, files being removed immediately once read in. (Clinical pathway)
    b. Second copy of file to me made to Research folder. (Research pathway)
3. Input directory must be be cleared of files that have been moved to both pathway folders 
4. NPIC ingection workflow must never be effected (blocked etc)
6. Minimise the 'interference' with file on clinical pathway to minimise risk to file contents, preferance give to OS file copy that is guaranteed bit perfect
7. Minimise delay on primary pathway
8. design to eliminate clinical risk
