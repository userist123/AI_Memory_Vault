<?php

add_action( 'rest_api_init', function () {
	register_rest_route( 'custom', '/customrelatedprojects', array(
		'methods'   =>  'GET',
		'callback'  =>  'get_custom_related_projects',
	) );
});
function get_custom_related_projects( $request ) {
	//$project_category = $_GET['category'];
	$current_project = $_GET['current'];
	$projects = get_field('custom_related_projects',($current_project));

	if($projects){
		$posts = $projects;
	}else{
		$posts = get_posts(
			array(
				'posts_per_page' => 3,
				'post_type' => 'projects',
				'exclude' => array($current_project),
				// 'ignore_custom_sort' => true,
				// 'orderby' => 'rand',
				// 'order'     => 'ASC'
			)
		);
	}

	// if($project_category === 'all'){
	// 	$posts = get_posts(
	// 		array(
	// 			'posts_per_page' => 3,
	// 			'post_type' => 'projects',
	// 			'exclude' => array($current_project),
	// 			'ignore_custom_sort' => true,
	// 			'orderby' => 'rand',
	// 			'order'     => 'ASC'
	// 		)
	// 	);
	// } else{
	// 	$posts = get_posts(
	// 		array(
	// 			'posts_per_page' => 3,
	// 			'post_type' => 'projects',
	// 			'exclude' => array($current_project),
	// 			'ignore_custom_sort' => true,
	// 			'orderby' => 'rand',
	// 			'order'     => 'ASC',
	// 			'tax_query' => array(
	// 				array(
	// 					'taxonomy' => 'project_categories',
	// 					'field' => 'slug',
	// 					'terms' => $project_category,
	// 				)
	// 			)
	// 		)
	// 	);	
	// }

	if ( empty( $posts ) ) {
		return null;
	}

	foreach( $posts as $post ) {
		$post_id = $post->ID;
		$featured_image = get_the_post_thumbnail_url( $post_id, 'story' );
		$post->featured_image = $featured_image; 
		$post->link = get_permalink( $post_id ); 
	}


    //return $posts;

	return new WP_REST_Response( $posts, 200 );

}

?>