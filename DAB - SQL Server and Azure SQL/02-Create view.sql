USE Library
GO

CREATE OR ALTER VIEW vwBookDetails
AS
	WITH AggregatedAuthorsCte AS
	(
		SELECT 
			ba.BookId,
			STRING_AGG(CONCAT(a.FirstName, ' ', (a.MiddleName + ' '), a.LastName), ', ') AS Authors
		FROM
			BookAuthor AS ba 
			INNER JOIN Author AS a ON a.AuthorId = ba.AuthorId
		GROUP BY
			ba.BookId    
	)
	SELECT
		b.BookId,
		b.Title,
		b.Year,
		b.Pages,
		AuthorCount = (SELECT COUNT(*) FROM BookAuthor AS ba WHERE ba.BookId = b.BookId),
		aa.Authors
	FROM
		Book AS b
		INNER JOIN AggregatedAuthorsCte AS aa ON b.BookId = aa.BookId
GO

SELECT * FROM vwBookDetails
